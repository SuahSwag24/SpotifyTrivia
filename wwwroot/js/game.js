document.addEventListener("DOMContentLoaded", () => {
    const container = document.querySelector(".game-container");
    const lobbyCode = container.dataset.lobbyCode;
    const playerId = container.dataset.playerId;
    const displayName = container.dataset.displayName;
    const isHost = container.dataset.isHost === "true";

    const connection = createLobbyConnection();
    const audio = document.getElementById("preview-audio");

    const previousScores = new Map();

    const volumeSlider = document.getElementById("volume-slider");
    const volumeIcon = document.getElementById("volume-icon");

    const savedVolume = localStorage.getItem("triviaVolume");
    const initialVolume = savedVolume !== null ? parseInt(savedVolume, 10) : 30;

    const albumCover = document.getElementById("album-cover");
    const revealAlbumCover = document.getElementById("reveal-album-cover");

    const errorBox = document.getElementById("game-error");
    function showError(message) {
        errorBox.textContent = message;
        errorBox.style.display = "block";
    }

    function addKickButton(playerElement, targetPlayerId, targetDisplayName) {
        if (!isHost || targetPlayerId === playerId || playerElement.querySelector("[data-target-player]")) return;

        const button = document.createElement("button");
        button.type = "button";
        button.className = "kick-player-btn";
        button.dataset.targetPlayer = targetPlayerId;
        button.dataset.targetName = targetDisplayName;
        button.title = `Remove ${targetDisplayName} from the game`;
        button.setAttribute("aria-label", `Remove ${targetDisplayName} from the game`);
        button.innerHTML = '<span aria-hidden="true">&#x22EE;</span>';
        playerElement.appendChild(button);
    }

    function removePlayerFromList(targetPlayerId) {
        document.querySelector(`#side-player-list [data-player-id="${CSS.escape(targetPlayerId)}"]`)?.remove();
    }

    const kickModalElement = document.getElementById("kick-confirmation-modal");
    const kickModal = bootstrap.Modal.getOrCreateInstance(kickModalElement);
    const kickMessage = document.getElementById("kick-confirmation-message");
    const confirmKickButton = document.getElementById("confirm-kick-btn");
    let pendingKick = null;

    function confirmKickPlayer(targetPlayerId, targetDisplayName) {
        pendingKick = { targetPlayerId, targetDisplayName };
        kickMessage.textContent = `Remove ${targetDisplayName} from the game?`;
        confirmKickButton.disabled = false;
        confirmKickButton.textContent = "Remove Player";
        kickModal.show();
    }

    confirmKickButton.addEventListener("click", async () => {
        if (!pendingKick) return;

        const { targetPlayerId, targetDisplayName } = pendingKick;
        confirmKickButton.disabled = true;
        confirmKickButton.textContent = "Removing...";

        try {
            await connection.invoke("KickPlayer", lobbyCode, targetPlayerId);
            removePlayerFromList(targetPlayerId);
            kickModal.hide();
        } catch (err) {
            showError("Failed to remove player: " + err);
            confirmKickButton.disabled = false;
            confirmKickButton.textContent = "Remove Player";
        }
    });

    document.getElementById("side-player-list").addEventListener("click", event => {
        const button = event.target.closest("[data-target-player]");
        if (!button) return;

        confirmKickPlayer(button.dataset.targetPlayer, button.dataset.targetName);
    });

    function showPhase(id) {
        document.querySelectorAll(".phase-panel").forEach(p => p.style.display = "none");
        document.getElementById(id).style.display = "block";
    }

    const continueGameBtn = document.getElementById("continue-game-btn");
    continueGameBtn.addEventListener("click", () => {
        if (!isHost) return;
        
        continueGameBtn.disabled = true;
        continueGameBtn.textContent = "Loading next round...";

        connection.invoke("ContinueGame", lobbyCode)
            .catch(err => {
                showError("Failed to continue: " + err);
                continueGameBtn.disabled = false;
                continueGameBtn.textContent = "Continue Game";
            })
    })

    setupLobbyHandlers(connection, {
        onCountdownStarted: (data) => {
            showPhase("countdown-phase");
            runLocalCountdown(data.startedAtUtc, data.seconds);

            previousScores.clear();

            document.getElementById("prompt-text").textContent = `${data.prompt}`;
        },
        onRoundStarted: (data) => {
            showPhase("question-phase");
            document.querySelectorAll("#side-player-list .player-pill-item")
                .forEach(li => li.classList.remove("has-answered", "answer-correct", "answer-incorrect"));


            const roundPercent = Math.min(100, Math.round(((data.questionNumber - 1) / data.totalQuestions) * 100))

            albumCover.src = data.albumCoverUrl;

            const visibility = (data.blurAlbum ?? "hide").toString().toLowerCase();
            const albumPlaceholder = document.getElementById("album-cover-hidden-placeholder");

            // Reset to default visible state first
            albumCover.style.display = "block";
            albumCover.classList.remove("album-blurred");
            if (albumPlaceholder) albumPlaceholder.style.display = "none";

            if (visibility === "hide" || visibility === "2") {
                albumCover.style.display = "none";
                if (albumPlaceholder) albumPlaceholder.style.display = "flex";
            } else if (visibility === "blur" || visibility === "1") {
                albumCover.classList.add("album-blurred");
            }

            document.getElementById("round-counter").textContent = `Question ${data.questionNumber}/${data.totalQuestions}`;
            document.getElementById('progress-fill').style.width = roundPercent + '%';

            audio.src = data.previewUrl;
            audio.currentTime = 0;
            audio.play().catch(() => console.log("Autoplay blocked — user interaction required."));

            renderAnswerChoices(data.answerChoices);
            runRoundTimer(data.startedAtUtc, data.durationSeconds);
        },
        onRoundEnded: (data) => {
            showPhase("reveal-phase");
            
            revealAlbumCover.src = data.albumCoverUrl;
            
            const startRevealAnimation = () => {
                void revealAlbumCover.offsetWidth;
                revealAlbumCover.classList.add("reveal-animation");
            };
            
            if (revealAlbumCover.complete) {
                startRevealAnimation();
            } else {
                revealAlbumCover.onload = startRevealAnimation;
            }
            
            document.getElementById("correct-answer-label").textContent = `Correct answer: ${data.correctAnswer}`;
            renderScoreboard(data.players);
        },
        onGameEnded: (leaderboard, songResults) => {
            showPhase("finished-phase");
            renderLeaderboard(leaderboard);
            renderSongListBoard(leaderboard, songResults);

            const returnToLobbyBtn = document.getElementById("return-to-lobby-btn");

            if (isHost) {
                returnToLobbyBtn.style.display = "inline-block";
                continueGameBtn.style.display = "inline-block";
                continueGameBtn.disabled = false;
                continueGameBtn.textContent = "Continue Playing";
            }
        },
        onActionError: (data) => {
            const continueGameBtn = document.getElementById("continue-game-btn");
            if (continueGameBtn && data.message.includes("No more unplayed tracks")) {
                continueGameBtn.disabled = true;
                continueGameBtn.textContent = "No More Songs Left.";
                return;
            }

            showError(data.message)
        },
        onLobbyDisbanded: () => { window.location.href = "/multiplayer"; },
        onPlayerKicked: (data) => {
            removePlayerFromList(data.playerId);
            showToast(`${data.displayName} was removed from the game`, "warning");
        },
        onKickedFromLobby: (data) => {
            showToast(data.message, "danger");
            setTimeout(() => {
                window.location.href = "/multiplayer";
            }, 800);
        },
        onPlayerJoined: (data) => {
            showToast(`${data.displayName} joined the game`, "success");
            if (!document.querySelector(`#side-player-list [data-player-id="${data.playerId}"]`)) {
                const li = document.createElement("li");
                li.className = "player-pill-item";
                li.dataset.playerId = data.playerId;
                li.innerHTML = `<span class="player-dot status-active"></span><span class="player-name">${data.displayName}</span><span class="player-status-text active">Pondering...</span>`;
                addKickButton(li, data.playerId, data.displayName);
                document.getElementById("side-player-list").appendChild(li);
            }
        },
        onPlayerLeft: (data) => {
            showToast(`${data.displayName} left the game`, "warning");
            document.querySelector(`#side-player-list [data-player-id="${data.playerId}"]`)?.remove();
        },
        onReturnedToLobby: () => {
            window.location.href = `/multiplayer/lobby/${lobbyCode}`;
        },
        onPlayerAnswered: (data) => {
            updatePlayerStatus(data.playerId, "answered");
        },
        onPlayerDisconnected: (data) => {
            showToast(`${data.displayName} disconnected from the game`, "warning");
            updatePlayerStatus(data.playerId, "disconnected");
        },
        onPlayerStatusChanged: (data) => {
            updatePlayerStatus(data.playerId, data.status);
        }
    });

    connection.onreconnecting((error) => {
        console.warn("SignalR reconnecting...", {
            error,
            lobbyCode,
            playerId,
            connectionState: connection.state
        });
        showToast("Connection lost, reconnecting...", "warning");
    });

    connection.onreconnected(async (connectionId) => {
        console.log("SignalR reconnected", {
            connectionId,
            lobbyCode,
            playerId
        });
        try {
            await syncServerTime(connection);
            await connection.invoke("JoinLobby", lobbyCode, playerId, displayName);
            await connection.invoke("RequestGamePhase", lobbyCode);
            showToast("Reconnected!", "success");
        } catch (err) {
            console.error("Failed to re-join after reconnect attempt:", err);
        }
    });

    connection.onclose((error) => {
        console.error("SignalR connection closed permanently:", {
            error,
            lobbyCode,
            playerId,
            connectionState: connection.state
        });
        showError("Connection lost. Please refresh the page.");
    });

    connection.start()
        .then(async () => {
            await syncServerTime(connection)
            setInterval(() => syncServerTime(connection), 10000);
        })
        .then(() => connection.invoke("JoinLobby", lobbyCode, playerId, displayName))
        .then(() => connection.invoke("RequestGamePhase", lobbyCode))
        .catch(err => console.error(err));

    function renderAnswerChoices(choices) {
        const container = document.getElementById("answer-choices");
        container.innerHTML = "";

        choices.forEach((choiceText, index) => {
            const btn = document.createElement("button");
            btn.textContent = choiceText;
            btn.className = "answer-btn";
            btn.dataset.index = index;
            btn.addEventListener("click", () => handleAnswerSelected(index, btn));
            container.appendChild(btn);
        });
    }

    function handleAnswerSelected(index, btn) {
        const allButtons = document.querySelectorAll("#answer-choices .answer-btn");
        allButtons.forEach(b => b.disabled = true);

        btn.classList.add("selected"); // optional: immediate feedback while waiting on server

        connection.invoke("SubmitAnswer", lobbyCode, playerId, index)
            .catch(err => console.error("Answer submit failed:", err));
    }

    let serverTimeOffset = 0;

    async function syncServerTime(connection) {
        const clientSentAt = Date.now();
        const serverUtcNow = await connection.invoke("GetServerTimeUtc");
        const clientReceivedAt = Date.now();

        const tripTime = clientReceivedAt - clientSentAt;
        const serverTime = new Date(serverUtcNow).getTime() + tripTime / 2;
        serverTimeOffset = serverTime - clientReceivedAt;
    }

    function runLocalCountdown(startedAtUtc, totalSeconds) {
        const el = document.getElementById("countdown-number");
        const startTime = new Date(startedAtUtc).getTime();
        let interval;

        function tick() {
            const elapsed = ((Date.now() + serverTimeOffset) - startTime) / 1000;
            const remaining = Math.min(
                totalSeconds,
                Math.max(0, Math.ceil(totalSeconds - elapsed))
            );
            el.textContent = remaining;
            if (remaining <= 0) clearInterval(interval);
        }

        tick();
        interval = setInterval(tick, 1000);
    }

    function runRoundTimer(startedAtUtc, totalSeconds) {
        const el = document.getElementById("round-timer");
        const startTime = new Date(startedAtUtc).getTime();
        let interval;

        function tick() {
            const elapsed = ((Date.now() + serverTimeOffset) - startTime) / 1000;
            const remaining = Math.min(
                totalSeconds,
                Math.max(0, Math.ceil(totalSeconds - elapsed))
            );
            el.textContent = remaining;
            if (remaining <= 0) clearInterval(interval);
        }

        tick();
        interval = setInterval(tick, 1000);
    }

    function renderScoreboard(players) {
        const list = document.getElementById("reveal-scoreboard");

        const oldPositions = new Map(
            [...list.children].map(li => [li.dataset.playerId, li.getBoundingClientRect()])
        );

        list.innerHTML = "";

        const sorted = players.slice().sort((a, b) => b.score - a.score);

        sorted.forEach((p, index) => {
            const li = document.createElement("li");
            li.dataset.playerId = p.playerId;
            li.classList.add("reveal-row");
            li.style.animationDelay = `${index * 70}ms`;

            if (p.playerId === playerId) {
                li.classList.add("is-current-player");
            }

            let stateClass = "state-noanswer";
            if (p.lastAnswerCorrect === true) stateClass = "state-correct";
            else if (p.lastAnswerCorrect === false) stateClass = "state-incorrect";
            li.classList.add(stateClass);

            const rank = index + 1;
            const rankClass = rank === 1 ? "rank-gold" : rank === 2 ? "rank-silver" : rank === 3 ? "rank-bronze" : "";

            const resultTag = p.lastAnswerCorrect === true ? "✅" : (p.lastAnswerCorrect === false ? "❌" : "—");
            const penaltyTag = p.lastAnswerPenalized ? ` <span class="penalty-tag">(-20% own song)</span>` : "";

            const prevScore = previousScores.has(p.playerId) ? previousScores.get(p.playerId) : p.score - (p.scoreDelta ?? 0);
            const delta = p.score - prevScore;
            const deltaTag = delta > 0 ? ` <span class="score-delta">+${delta}</span>` : "";

            li.innerHTML = `
                <span class="row-left">
                    <span class="rank-badge ${rankClass}">#${rank}</span> ${resultTag} ${p.displayName}
                </span>
                <span class="row-right">
                    <span class="score-value">${prevScore}</span>${deltaTag}${penaltyTag}
                </span>
            `;

            list.appendChild(li);
        });

        const rowsToAnimate = [];
        [...list.children].forEach(li => {
            const oldRect = oldPositions.get(li.dataset.playerId);
            if (!oldRect) return;

            const newRect = li.getBoundingClientRect();
            const deltaY = oldRect.top - newRect.top;
            if (deltaY === 0) return;

            li.style.animation = "none";
            li.style.opacity = "1";
            li.style.transform = `translateY(${deltaY}px)`;
            li.style.transition = "none";

            rowsToAnimate.push(li);
        });

        setTimeout(() => {
            rowsToAnimate.forEach(li => {
                li.style.transform = "translateY(0)";
                li.style.transition = "transform 0.6s cubic-bezier(0.22, 1, 0.36, 1)";
            });

            sorted.forEach(p => {
                const li = list.querySelector(`[data-player-id="${p.playerId}"]`);
                const scoreEl = li?.querySelector(".score-value");
                const prevScore = previousScores.has(p.playerId)
                    ? previousScores.get(p.playerId)
                    : p.score - (p.scoreDelta ?? 0);

                if (scoreEl && prevScore !== p.score) {
                    animateScoreCount(scoreEl, prevScore, p.score);
                }
            });

            sorted.forEach(p => previousScores.set(p.playerId, p.score));
        }, 500);
    }

    function renderLeaderboard(players) {
        const list = document.getElementById("final-leaderboard");
        list.innerHTML = "";
        players
            .slice()
            .sort((a, b) => b.score - a.score)
            .forEach(p => {
                const li = document.createElement("li");
                li.textContent = `${p.displayName} — ${p.score}`;
                list.appendChild(li);
            });
    }

    async function leaveAndRedirect() {
        const confirmed = await showLeaveConfirmation(isHost);
        if (!confirmed) return;

        const leaveButtons = [
            document.getElementById("leave-game-btn"),
            document.getElementById("leave-results-btn")
        ].filter(Boolean);

        leaveButtons.forEach(button => button.disabled = true);

        connection.invoke("LeaveLobby", lobbyCode, playerId)
            .catch(err => console.error("Leave failed:", err))
            .finally(() => { window.location.href = "/multiplayer" });
    }
    document.getElementById("leave-game-btn")?.addEventListener("click", leaveAndRedirect);
    document.getElementById("leave-results-btn")?.addEventListener("click", leaveAndRedirect);

    document.getElementById("return-to-lobby-btn").addEventListener("click", () => {
        connection.invoke("ReturnToLobby", lobbyCode)
            .catch(err => showError("Failed to return to lobby: " + err));
    })

    function renderSongListBoard(leaderboard, songResults) {
        const sortedPlayers = leaderboard.slice().sort((a, b) => b.score - a.score);
        const list = document.getElementById("song-list-board");
        list.innerHTML = "";

        songResults.forEach((song, i) => {
            const li = document.createElement("li");
            li.className = "song-recap-item";

            const me = sortedPlayers.find(p => p.playerId === playerId);
            const myAnswer = me?.answerHistory[i];
            const myVerdict = myAnswer?.wasCorrect ? "✅" : "❌";

            const tooltipRows = sortedPlayers.map(p => {
                const answer = p.answerHistory[i];
                const icon = answer ? (answer.wasCorrect ? "✅" : "❌") : "—";
                return `<div class="verdict-row"><span>${p.displayName}</span><span>${icon}</span></div>`;
            }).join("");

            const contributorLabel = buildContributorLabel(song.contributedBy);

            const playButtonElement = song.previewUrl
                ? `<button class="recap-play-btn" data-preview-url="${song.previewUrl}" data-song-index="${i}">Play</button>`
                : "";

            li.innerHTML = `
                <div class="song-recap-info">
                    ${playButtonElement}
                    <div class="song-recap-text">
                        <a href="${song.spotifyUrl}" target="_blank" class="song-recap-title">${song.songTitle}</a>
                        <span class="song-recap-artist">${song.artistName}</span>
                        ${contributorLabel}
                    </div>
                </div>
                <div class="song-recap-verdict">
                    <span class="my-verdict">${myVerdict}</span>
                    <div class="verdict-tooltip">${tooltipRows}</div>
                </div>
            `;
            list.appendChild(li);
        });

        list.querySelectorAll(".recap-play-btn").forEach(btn => {
            btn.addEventListener("click", () => handleRecapPlay(btn));
        });
    }

    function buildContributorLabel(contributedBy) {
        if (!contributedBy || contributedBy.length === 0) return "";

        const MAX_NAMES_SHOWN = 3;

        if (contributedBy.length <= MAX_NAMES_SHOWN) {
            return `<span class="song-recap-source">From ${contributedBy.join(", ")}</span>`;
        }

        const shown = contributedBy.slice(0, MAX_NAMES_SHOWN);
        const remaining = contributedBy.length - MAX_NAMES_SHOWN;
        return `<span class="song-recap-source">From ${shown.join(", ")} +${remaining} more</span>`;
    }

    function animateScoreCount(el, from, to, duration = 600) {
        const start = performance.now();
        function tick(now) {
            const progress = Math.min((now - start) / duration, 1);
            const eased = 1 - Math.pow(1 - progress, 3); // ease-out cubic
            el.textContent = Math.round(from + (to - from) * eased);
            if (progress < 1) requestAnimationFrame(tick);
        }
        requestAnimationFrame(tick);
    }

    volumeSlider.value = initialVolume;
    audio.volume = initialVolume / 100;
    updateVolumeIcon(initialVolume);

    volumeSlider.addEventListener("input", () => {
        const value = parseInt(volumeSlider.value, 10);
        audio.volume = value / 100;
        localStorage.setItem("triviaVolume", value);
        updateVolumeIcon(value);
    });

    function updateVolumeIcon(value) {
        volumeIcon.textContent = value === 0 ? "🔇" : value < 50 ? "🔉" : "🔊";
    }

    let currentlyPlayingBtn = null;
    function handleRecapPlay(btn) {
        const url = btn.dataset.previewUrl;

        if (currentlyPlayingBtn === btn) {
            if (audio.paused) {
                if (audio.ended) {
                    audio.currentTime = 0;
                }

                audio.play();
                btn.textContent = "Pause";
            } else {
                audio.pause();
                btn.textContent = "Play";
            }

            return;
        }

        if (currentlyPlayingBtn) {
            currentlyPlayingBtn.textContent = "Play";
        }

        audio.src = url;
        audio.currentTime = 0;

        audio.play()
            .then(() => {
                btn.textContent = "Pause";
                currentlyPlayingBtn = btn;
            })
            .catch(() => {
                showToast("This preview is no longer available.", "warning");
                btn.disabled = true;
                btn.textContent = "Unavailable";
                currentlyPlayingBtn = null;
            });
    }

    function updatePlayerStatus(playerId, status) {
        const rootList = document.getElementById("side-player-list");
        if (!rootList) return;

        const playerEl = rootList.querySelector(`[data-player-id="${playerId}"]`);
        if (!playerEl) return;

        const normalized = String(status).toLowerCase();

        const dotEl = playerEl.querySelector('.player-dot');
        if (dotEl) {
            dotEl.classList.remove('status-active', 'status-answered', 'status-disconnected');
            dotEl.classList.add(`status-${normalized}`);
        }

        const textEl = playerEl.querySelector('.player-status-text');
        if (textEl) {
            const statusText = normalized === 'answered' ? 'Answered' : normalized === 'disconnected' ? 'Disconnected' : 'Pondering...';
            textEl.textContent = statusText;
            textEl.classList.remove('active', 'answered', 'disconnected');
            textEl.classList.add(normalized);
        }

        playerEl.classList.toggle('has-answered', normalized === 'answered');
    }
});