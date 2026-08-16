function createLobbyConnection() {
    return new signalR.HubConnectionBuilder()
        .withUrl("/hubs/lobby")
        .withAutomaticReconnect()
        .build();
}

function setupLobbyHandlers(connection, callbacks) {
    connection.on("PlayerJoined", (data) => callbacks.onPlayerJoined?.(data));
    connection.on("PlayerLeft", (data) => callbacks.onPlayerLeft?.(data));
    connection.on("PlayerDisconnected", (data) => callbacks.onPlayerDisconnected?.(data));
    connection.on("PlaylistSelected", (data) => callbacks.onPlaylistSelected?.(data));
    connection.on("LobbyDisbanded", () => callbacks.onLobbyDisbanded?.());
    connection.on("ActionError", (data) => callbacks.onActionError?.(data));

    connection.on("CountdownStarted", (data) => callbacks.onCountdownStarted?.(data));
    connection.on("RoundStarted", (data) => callbacks.onRoundStarted?.(data));
    connection.on("RoundEnded", (data) => callbacks.onRoundEnded?.(data));
    connection.on("GameEnded", (data) => callbacks.onGameEnded?.(data));
}