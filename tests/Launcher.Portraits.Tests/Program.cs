using System.Buffers.Binary;
using Launcher.Helpers;

var scratch=Path.Combine(Path.GetTempPath(),"PortraitSessionTests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
int tick=0,passed=0;
static void Check(bool value,string text){if(!value)throw new Exception(text);}
byte[] Image(byte content=0,int width=70,int height=70){var b=new byte[60];new byte[]{137,80,78,71,13,10,26,10}.CopyTo(b,0);"IHDR"u8.CopyTo(b.AsSpan(12));BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16),(uint)width);BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20),(uint)height);b[40]=content;return b;}
string Dir(){var p=Path.Combine(scratch,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
void Write(string dir,string guid,byte[] bytes){var p=Path.Combine(dir,guid+".png");File.WriteAllBytes(p,bytes);File.SetLastWriteTimeUtc(p,DateTime.UtcNow.AddSeconds(++tick));}
async Task Test(string name,Func<Task> action){await action();passed++;Console.WriteLine("PASS "+name);}
await Test("ExistingFilesNeverUploadWithoutFreshNativeCapture",async()=>{
 var dir=Dir();Write(dir,"17",Image());int calls=0;using var session=new PortraitSyncSession(dir,(g,b,t)=>{calls++;return Task.FromResult(true);});
 await session.ScanAsync();Check(calls==0,"Uploaded an older account/session headshot");
 Write(dir,"17",Image(1));await session.ScanAsync();Check(calls==1,"Fresh capture did not upload");
});
await Test("MissingDirectoryAppearingAfterLaunchIsObserved",async()=>{
 var dir=Path.Combine(scratch,Guid.NewGuid().ToString("N"));int calls=0;using var session=new PortraitSyncSession(dir,(g,b,t)=>{Check(g=="33","Wrong GUID");calls++;return Task.FromResult(true);});
 await session.ScanAsync();Directory.CreateDirectory(dir);Write(dir,"33",Image());await session.ScanAsync();Check(calls==1,"Missed post-launch capture");
});
await Test("IdenticalRewritesAreCoalescedButChangedBytesUpload",async()=>{
 var dir=Dir();int calls=0;using var session=new PortraitSyncSession(dir,(g,b,t)=>{calls++;return Task.FromResult(true);});
 Write(dir,"17",Image(1));await session.ScanAsync();Write(dir,"17",Image(1));await session.ScanAsync();Write(dir,"17",Image(2));await session.ScanAsync();Check(calls==2,"Incorrect content coalescing");
});
await Test("FailedUploadIsRetriedWithoutAnotherFileRewrite",async()=>{
 var dir=Dir();int calls=0;var time=new TestTime();using var session=new PortraitSyncSession(dir,(g,b,t)=>Task.FromResult(++calls>1),time);
 Write(dir,"17",Image());await session.ScanAsync();await session.ScanAsync();Check(calls==1,"Failed upload retried without backoff");time.Now=time.Now.AddMinutes(1);await session.ScanAsync();await session.ScanAsync();Check(calls==2,"Failed image was marked sent or accepted image resent");
});
await Test("FileChangesDuringUploadCannotReplaceCapturedBytesOrLoseNextUpdate",async()=>{
 var dir=Dir();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var seen=new List<byte>();
 using var session=new PortraitSyncSession(dir,async(g,b,t)=>{seen.Add(b[40]);if(seen.Count==1){entered.SetResult();await release.Task.WaitAsync(t);}return true;});
 Write(dir,"17",Image(1));var first=session.ScanAsync();await entered.Task;Write(dir,"17",Image(2));var second=session.ScanAsync();Check(!second.IsCompleted,"Scans overlapped");release.SetResult();await Task.WhenAll(first,second);Check(seen.SequenceEqual(new byte[]{1,2}),"Newer capture lost or upload reread mutable file");
});
await Test("InvalidNamesPartialFilesAndWrongDimensionsDoNotUpload",async()=>{
 var dir=Dir();int calls=0;using var session=new PortraitSyncSession(dir,(g,b,t)=>{calls++;return Task.FromResult(true);});
 foreach(var name in new[]{"1","0","32","abc","017"})Write(dir,name,Image());Write(dir,"17",new byte[10]);Write(dir,"33",Image(width:71));Write(dir,"49",Image(height:71));await session.ScanAsync();Check(calls==0,"Invalid headshot uploaded");
 Write(dir,"17",Image());await session.ScanAsync();Check(calls==1,"Completed file did not recover");
});
await Test("DisposeCancelsInFlightUploadAndStopsLaterWork",async()=>{
 var dir=Dir();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int calls=0;using var session=new PortraitSyncSession(dir,async(g,b,t)=>{calls++;entered.SetResult();await Task.Delay(Timeout.Infinite,t);return true;});
 Write(dir,"17",Image());var scan=session.ScanAsync();await entered.Task;session.Dispose();try{await scan;throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
 try{await session.ScanAsync();throw new Exception("Disposed session continued");}catch(OperationCanceledException){}Check(calls==1,"Upload after exit");
});
await Test("NewSessionRequiresNewCaptureAndDoesNotInheritPreviousAck",async()=>{
 var dir=Dir();int calls=0;using(var first=new PortraitSyncSession(dir,(g,b,t)=>{calls++;return Task.FromResult(true);})){Write(dir,"17",Image());await first.ScanAsync();}
 using var next=new PortraitSyncSession(dir,(g,b,t)=>{calls++;return Task.FromResult(true);});await next.ScanAsync();Check(calls==1,"Old file uploaded during next login");Write(dir,"17",Image());await next.ScanAsync();Check(calls==2,"Fresh same-byte capture missed on next session");
});
await Test("LargeOldCacheCannotHideTheActiveCharacter",async()=>{
 var dir=Dir();for(var i=1;i<=150;i++)Write(dir,(16*i+1).ToString(),Image());var seen=new List<string>();
 using var session=new PortraitSyncSession(dir,(g,b,t)=>{seen.Add(g);return Task.FromResult(true);});await session.ScanAsync();Check(seen.Count==0,"Old cached portraits were queued");
 Write(dir,"9999999999999999985",Image(2));await session.ScanAsync();Check(seen.SequenceEqual(new[]{"9999999999999999985"}),"Active character beyond old cache limit was starved");
});
await Test("InvalidPendingImagesCannotStarveValidCaptures",async()=>{
 var dir=Dir();var seen=new List<string>();using var session=new PortraitSyncSession(dir,(g,b,t)=>{seen.Add(g);return Task.FromResult(true);});
 for(var i=1;i<=24;i++)Write(dir,(16*i+1).ToString(),new byte[60]);Write(dir,"9999999999999999985",Image());
 for(var i=0;i<5;i++)await session.ScanAsync();Check(seen.Count==1,"Invalid pending files starved valid update");
});
await Test("NativeLowercaseDirectoryCreatedLaterIsDiscovered",async()=>{
 var root=Dir();var native=Path.Combine(root,"Resources","images","heads");int calls=0;
 using var session=new PortraitSyncSession(Path.Combine(root,"Resources","Images","heads"),(g,b,t)=>{calls++;return Task.FromResult(true);});
 await session.ScanAsync();Directory.CreateDirectory(native);Write(native,"17",Image());await session.ScanAsync();Check(calls==1,"Native case variation missed");
});
Console.WriteLine($"{passed} passed. Evidence scratch: {scratch}");
sealed class TestTime:TimeProvider {public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
