document.addEventListener("DOMContentLoaded", () => {
    const container = document.querySelector(".lobby-container");
    const lobbyCode = container.dataset.lobbyCode;
    const playerId = container.dataset.playerId;
    const isHost = container.dataset.isHost === "true";

    const connection = createLobbyConnection();

    const errorBox = document.getElementById("lobby-error");
    function showError(message) {
        errorBox.textContent = message;
        errorBox.style.display = "block";
    }

    setupLobbyHandlers(connection, {
        onPlayerJoined: (data) => {
            const list = document.getElementById("player-list");
            if (!list.querySelector(`[data-player-id="${data.playerId}"]`)) {
                const li = document.createElement("li");
                li.className = "player-pill-item";
                li.dataset.playerId = data.playerId;

                const icon = document.createElement("span");
                icon.textContent = "👤";

                const nameSpan = document.createElement("span");
                nameSpan.className = "text-truncate";
                nameSpan.textContent = data.displayName;

                li.appendChild(icon);
                li.appendChild(nameSpan);
                list.appendChild(li);
            }
        },
        onPlayerLeft: (data) => {
            document.querySelector(`#player-list [data-player-id="${data.playerId}"]`)?.remove();
        },
        onPlayerDisconnected: (data) => {
            document.querySelector(`#player-list [data-player-id="${data.playerId}"]`)?.classList.add("disconnected");
        },
        onPlaylistSelected: (data) => {
            document.getElementById("selected-playlist-label").textContent = `Selected: ${data.playlistId}`;
            document.getElementById("start-game-btn").disabled = false;
        },
        onActionError: (data) => showError(data.message),
        onLobbyDisbanded: () => { window.location.href = "/multiplayer"; },
        onCountdownStarted: () => { window.location.href = `/multiplayer/game/${lobbyCode}`; }
    });

    connection.on("JoinStatus", (data) => {
        setWaitingBarText(data.status);
    });


    connection.start()
        .then(() => connection.invoke("JoinLobby", lobbyCode, playerId, "Player"))
        .catch(err => showError("Connection failed: " + err));

    if (isHost) {
        const chooseBtn = document.getElementById("choose-playlist-btn");
        const pickerContainer = document.getElementById("playlist-picker-container");
        const startBtn = document.getElementById("start-game-btn");

        let selectedQuestionCount = 10;

        const questionSlider = document.getElementById("question-count-slider");
        const questionDisplay = document.getElementById("question-count-display");
        const saveSettingsBtn = document.getElementById("save-settings-btn");

        saveSettingsBtn.addEventListener("click", () => {
            selectedQuestionCount = parseInt(questionSlider.value, 10);
        })

        chooseBtn.addEventListener("click", async () => {
            const res = await fetch("/playlists/picker-partial");
            pickerContainer.innerHTML = await res.text();
            pickerContainer.style.display = "block";
        });

        pickerContainer.addEventListener("click", async (e) => {
            const btn = e.target.closest(".playlist-select-btn");
            if (!btn) return;

            const playlistId = btn.dataset.playlistId;
            const playlistName = btn.dataset.playlistName;

            document.getElementById("selected-playlist-label").textContent = `Selected playlist: ${playlistName}`;
            pickerContainer.style.display = "none";

            connection.invoke("SelectPlaylist", lobbyCode, playlistId)
                .catch(err => showError("Failed to select playlist: " + err));
        });

        startBtn.addEventListener("click", () => {
            startBtn.disabled = true;
            connection.invoke("StartGame", lobbyCode, selectedQuestionCount)
                .catch(err => { showError("Failed to start: " + err); startBtn.disabled = false; });
        });
    }

    document.getElementById("leave-lobby-btn").addEventListener("click", () => {
        connection.invoke("LeaveLobby", lobbyCode, playerId)
            .catch(err => showError("Failed to leave: " + err))
            .finally(() => { window.location.href = "/multiplayer"; });
    });
});

function setWaitingBarText(status) {
    const bar = document.getElementById("waiting-message");
    if (!bar) return; // host has no waiting-message element

    if (status === "pendingJoin") {
        bar.textContent = "🎧 Round in progress — you'll join next round.";
    } else {
        bar.textContent = "⏳ Waiting for the host to select a playlist and start the game...";
    }
}