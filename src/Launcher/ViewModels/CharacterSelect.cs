using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Launcher.Helpers;
using Launcher.Models;

using NLog;

namespace Launcher.ViewModels;

/// <summary>
/// One character on the picker, with its headshot once it has been fetched.
/// </summary>
public partial class CharacterCard : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnknown))]
    private Bitmap? portrait;

    [ObservableProperty]
    private bool isSelected;

    public required CharacterInfo Info { get; init; }

    /// <summary>
    /// The "create one" tile rather than a real character. Choosing it launches the way
    /// the launcher always did, so the client shows its own creation flow.
    /// </summary>
    public bool IsNewCharacter { get; init; }

    public string Name => Info.Name;

    /// <summary>
    /// A real character with no headshot yet - the website showed a "?" tile for these.
    /// </summary>
    public bool ShowUnknown => !IsNewCharacter && Portrait is null;

    /// <summary>
    /// Shown in place of a headshot for a character who has never played.
    /// </summary>
    public string Initials
    {
        get
        {
            // The create-one tile shows a plus instead.
            if (IsNewCharacter)
                return string.Empty;

            var parts = Info.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
            };
        }
    }
}

/// <summary>
/// Login, pick a face, play - the way the retail site did it.
/// </summary>
public partial class CharacterSelect : Popup
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly Server _server;
    private readonly string _sessionId;
    private readonly Func<CharacterLaunchResponse?, Task> _launchAsync;

    [ObservableProperty]
    private CharacterCard? selected;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public AvaloniaList<CharacterCard> Characters { get; } = [];

    /// <summary>
    /// Set when the player chose a character and the server issued a ticket for it.
    /// </summary>
    public CharacterLaunchResponse? Launch { get; private set; }

    public CharacterSelect(
        Server server,
        string sessionId,
        IEnumerable<CharacterInfo> characters,
        Func<CharacterLaunchResponse?, Task> launchAsync)
    {
        _server = server;
        _sessionId = sessionId;
        _launchAsync = launchAsync;

        foreach (var info in characters.OrderByDescending(x => x.LastLoginAt ?? DateTimeOffset.MinValue))
            Characters.Add(new CharacterCard { Info = info });

        Selected = Characters.FirstOrDefault();

        // Without this an account that already has characters could never make another:
        // the picker replaces the in-game select screen, which is where you used to do it.
        Characters.Add(new CharacterCard
        {
            IsNewCharacter = true,
            Info = new CharacterInfo { Name = App.GetText("Text.CharacterSelect.New") }
        });

        View = new Views.CharacterSelect
        {
            DataContext = this
        };

        _ = LoadPortraitsAsync();
    }

    private async Task LoadPortraitsAsync()
    {
        foreach (var card in Characters.ToArray())
        {
            if (card.IsNewCharacter)
                continue;

            var bytes = await CharacterApi.TryGetPortraitAsync(card.Info.PortraitUrl, _sessionId);

            if (bytes is null || bytes.Length == 0)
                continue;

            try
            {
                using var stream = new MemoryStream(bytes);

                card.Portrait = new Bitmap(stream);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not decode the portrait for '{Name}'.", card.Info.Name);
            }
        }
    }

    [RelayCommand]
    private void Choose(CharacterCard card) => Selected = card;

    partial void OnSelectedChanged(CharacterCard? value)
    {
        foreach (var card in Characters)
            card.IsSelected = ReferenceEquals(card, value);
    }

    /// <summary>
    /// Asks the server to resolve the selection and issue a ticket. A refusal is not an
    /// error the player needs to see - it just means launching into the in-game screen.
    /// </summary>
    public override async Task<bool> ProcessAsync()
    {
        if (Selected is null)
            return true;

        // Launch with no character so the client shows its own flow, which is where
        // characters are created.
        if (Selected.IsNewCharacter)
        {
            StatusMessage = App.GetText("Text.CharacterSelect.Creating");

            await _launchAsync(null);

            return true;
        }

        StatusMessage = App.GetText("Text.CharacterSelect.Starting");

        Launch = await CharacterApi.TryLaunchCharacterAsync(
            _server.Info.WebApiUrl, _sessionId, Selected.Info.Guid);

        if (Launch is null)
            _logger.Warn("Server would not issue a launch ticket for '{Name}'; launching normally.", Selected.Info.Name);

        await _launchAsync(Launch);

        return true;
    }
}
