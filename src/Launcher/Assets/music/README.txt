Put the launcher's music here as theme.mp3.

MusicService looks for Assets/music/theme.mp3 and plays it quietly on a loop while the
launcher is open, at about 18% volume. Without a file here it does nothing and logs one
line saying so - that is the normal state until a track is chosen.

Any format the system can decode works; rename it to theme.mp3 or change TrackUri in
Services/MusicService.cs.

Whatever goes here ships inside the launcher and is downloaded by every player, so it
needs to be something you have the right to distribute.
