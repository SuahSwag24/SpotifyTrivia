document.addEventListener("DOMContentLoaded", () => {
    const container = document.querySelector(".lobby-container");
    const lobbyCode = container.dataset.lobbyCode;
    const playerId = container.dataset.playerId;
    const displayName = container.dataset.displayName;
    const isHost = container.dataset.isHost === "true";

    const connection = createLobbyConnection();

    const errorBox = document.getElementById("lobby-error");
    function showError(message) {
        errorBox.textContent = message;
        errorBox.style.display = "block";
    }

    const leaveBtn = document.getElementById("leave-lobby-btn");
    let resetStartControls = () => { };

    function applySelectedGameMode(mode) {
        try {
            const grid = document.getElementById("gamemode-grid");
            if (!grid) return;

            const cards = grid.querySelectorAll(".gamemode-card");
            cards.forEach(card => {
                card.classList.toggle("selected", card.dataset.mode === mode);
            });
        } catch (err) {
            console.error("Error applying game mode selection:", err);
        }
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

            showToast(`${data.displayName} joined the lobby`, "success");
        },
        onPlayerLeft: (data) => {
            document.querySelector(`#player-list [data-player-id="${data.playerId}"]`)?.remove();
            showToast(`${data.displayName} has left the lobby`, "warning");
        },
        onPlayerDisconnected: (data) => {
            document.querySelector(`#player-list [data-player-id="${data.playerId}"]`)?.classList.add("disconnected");
        },
        onPlaylistSelected: (data) => {
            document.getElementById("selected-playlist-label").textContent = `Selected: ${data.name}`;
            document.getElementById("start-game-btn").disabled = false;
        },
        onGameModeSelected: (data) => {
            applySelectedGameMode(data.mode);
        },
        onActionError: (data) => {
            resetStartControls();
            showError(data.message);
        },
        onLobbyDisbanded: () => { window.location.href = "/multiplayer"; },
        onCountdownStarted: () => { window.location.href = `/multiplayer/game/${lobbyCode}`; },
        onPreparingGame: () => {
            showToast("Preparing game, gathering song previews...", "success");
            leaveBtn.disabled = true;
        }
    });

    connection.on("JoinStatus", (data) => {
        setWaitingBarText(data.status);
        if (isHost && startBtn) startBtn.disabled = false;
    });


    connection.start()
        .then(() => {
            if (isHost) document.getElementById("start-game-btn").disabled = true;
            return connection.invoke("JoinLobby", lobbyCode, playerId, displayName)
        })
        .catch(err => showError("Connection failed: " + err));

    if (isHost) {
        const chooseBtn = document.getElementById("choose-playlist-btn");
        const pickerContainer = document.getElementById("playlist-picker-container");
        const startBtn = document.getElementById("start-game-btn");
        const settingsBtn = document.getElementById("game-settings-btn");
        const gameModeGrid = document.getElementById("gamemode-grid");

        let selectedQuestionCount = 10;
        let selectedRoundDurationSeconds = 10;

        const questionSlider = document.getElementById("question-count-slider");
        const questionDisplay = document.getElementById("question-count-display");

        const roundDurationSlider = document.getElementById("round-duration-slider");
        const roundDurationDisplay = document.getElementById("round-duration-display");

        const saveSettingsBtn = document.getElementById("save-settings-btn");

        resetStartControls = () => {
            startBtn.disabled = false;
            chooseBtn.disabled = false;
            settingsBtn.disabled = false;
            startBtn.textContent = "Start Game";
        };

        saveSettingsBtn.addEventListener("click", () => {
            selectedQuestionCount = parseInt(questionSlider.value, 10);
            selectedRoundDurationSeconds = parseInt(roundDurationSlider.value, 10);
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

            connection.invoke("SelectPlaylist", lobbyCode, playlistId, playlistName)
                .catch(err => showError("Failed to select playlist: " + err));
        });

        if (gameModeGrid) {
            gameModeGrid.addEventListener("click", async (e) => {
                const card = e.target.closest(".gamemode-card");
                if (!card) return;

                const mode = card.dataset.mode;
                const previouslySelected = gameModeGrid.querySelector(".gamemode-card.selected");

                applySelectedGameMode(mode);

                try {
                    await connection.invoke("SelectGameMode", lobbyCode, mode);
                } catch (err) {
                    showError("Failed to select game mode:", err);
                    applySelectedGameMode(previouslySelected?.dataset.mode ?? null);
                }
            })
        }

        startBtn.addEventListener("click", () => {
            startBtn.disabled = true;
            chooseBtn.disabled = true;
            settingsBtn.disabled = true;

            startBtn.textContent = "Starting...";
            connection.invoke("StartGame", lobbyCode, selectedQuestionCount, selectedRoundDurationSeconds)
                .catch(err => {
                    showError("Failed to start: " + err);
                    resetStartControls();
                });
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