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
    connection.on("GameModeSelected", (data) => callbacks.onGameModeSelected?.(data));
    connection.on("LobbyDisbanded", () => callbacks.onLobbyDisbanded?.());
    connection.on("ActionError", (data) => callbacks.onActionError?.(data));
    connection.on("PlayerAnswered", (data) => callbacks.onPlayerAnswered?.(data));

    connection.on("CountdownStarted", (data) => callbacks.onCountdownStarted?.(data));
    connection.on("PreparingGame", () => callbacks.onPreparingGame?.());
    connection.on("RoundStarted", (data) => callbacks.onRoundStarted?.(data));
    connection.on("RoundEnded", (data) => callbacks.onRoundEnded?.(data));
    connection.on("GameEnded", (leaderboard, songResults) => callbacks.onGameEnded?.(leaderboard, songResults));

    connection.on("AnswerResult", (data) => {
        const allButtons = document.querySelectorAll("#answer-choices .answer-btn");

        allButtons.forEach(b => b.classList.remove("selected"));

        const correctBtn = document.querySelector(`#answer-choices .answer-btn[data-index="${data.correctIndex}"]`);
        correctBtn?.classList.add("correct");

        if (!data.wasCorrect) {
            const yourBtn = document.querySelector(`#answer-choices .answer-btn[data-index="${data.submittedIndex}"]`);
            yourBtn?.classList.add("incorrect");
        }
    });

    connection.on("ReturnedToLobby", () => callbacks.onReturnedToLobby?.());
}

function showLeaveConfirmation(isHost) {
    const modalElement = document.getElementById("leave-confirmation-modal");
    const confirmButton = document.getElementById("confirm-leave-btn");
    const messageElement = document.getElementById("leave-confirmation-message");

    messageElement.textContent = isHost
        ? "Leaving will disband the lobby. Are you sure?"
        : "Are you sure you want to leave the game?";

    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);

    return new Promise(resolve => {
        let resolved = false;

        function finish(result) {
            if (resolved) return;

            resolved = true;
            confirmButton.onclick = null;
            resolve(result);
        }

        confirmButton.onclick = () => {
            finish(true);
            modal.hide();
        };

        modalElement.addEventListener(
            "hidden.bs.modal",
            () => finish(false),
            { once: true }
        );

        modal.show();
    })
}