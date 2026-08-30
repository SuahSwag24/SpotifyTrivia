using SpotifyTrivia.Models.Multiplayer;
using SpotifyTrivia.Services.GameModes;

public interface IGameModeFactory { IGameMode GetGameMode(GameModeType modeType); }

public class GameModeFactory : IGameModeFactory
{
    private readonly IEnumerable<IGameMode> _gameModes;
    public GameModeFactory(IEnumerable<IGameMode> gameModes) => _gameModes = gameModes;

    public IGameMode GetGameMode(GameModeType modeType) =>
        _gameModes.FirstOrDefault(m => m.ModeType == modeType)
            ?? throw new NotSupportedException($"No IGameMode registered for {modeType}");
}