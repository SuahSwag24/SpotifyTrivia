document.addEventListener("DOMContentLoaded", () => {
    const container = document.querySelector(".game-container");
    const lobbyCode = container.dataset.lobbyCode;
    const playerId = container.dataset.playerId;
    const displayName = container.dataset.displayName;
    const isHost = container.dataset.isHost === "true";

    const connection = createLobbyConnection();
    const audio = document.getElementById("preview-audio");

    const errorBox = document.getElementById("game-error");
    function showError(message) {
        errorBox.textContent = message;
        errorBox.style.display = "block";
    }

    function showPhase(id) {
        document.querySelectorAll(".phase-panel").forEach(p => p.style.display = "none");
        document.getElementById(id).style.display = "block";
    }

    setupLobbyHandlers(connection, {
        onCountdownStarted: (data) => {
            showPhase("countdown-phase");
            runLocalCountdown(data.startedAtUtc, data.seconds);

            document.getElementById("prompt-text").textContent = `${data.prompt}`;
        },
        onRoundStarted: (data) => {
            showPhase("question-phase");
            document.querySelectorAll("#side-player-list .player-pill-item")
                .forEach(li => li.classList.remove("has-answered", "answer-correct", "answer-incorrect"));


            const roundPercent = Math.min(100, Math.round(((data.questionNumber - 1) / data.totalQuestions) * 100))

            document.getElementById("album-cover").src = data.albumCoverUrl;

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
            }
        },
        onActionError: (data) => showError(data.message),
        onLobbyDisbanded: () => { window.location.href = "/multiplayer"; },
        onPlayerJoined: (data) => {
            showToast(`${data.displayName} joined the game`, "success");
            if (!document.querySelector(`#side-player-list [data-player-id="${data.playerId}"]`)) {
                const li = document.createElement("li");
                li.className = "player-pill-item";
                li.dataset.playerId = data.playerId;
                li.innerHTML = `<span class="player-dot"></span><span class="player-name">${data.displayName}</span>`;
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
            document.querySelector(`#side-player-list [data-player-id="${data.playerId}"]`)?.classList.add("has-answered");
        }
    });

    connection.start()
        .then(() => connection.invoke("JoinLobby", lobbyCode, playerId, "Player"))
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

    function runLocalCountdown(startedAtUtc, totalSeconds) {
        const el = document.getElementById("countdown-number");
        const startTime = new Date(startedAtUtc).getTime();
        let interval;

        function tick() {
            const elapsed = (Date.now() - startTime) / 1000;
            const remaining = Math.max(0, Math.ceil(totalSeconds - elapsed));
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
            const elapsed = (Date.now() - startTime) / 1000;
            const remaining = Math.max(0, Math.ceil(totalSeconds - elapsed));
            el.textContent = remaining;
            if (remaining <= 0) clearInterval(interval);
        }

        tick();
        interval = setInterval(tick, 1000);
    }

    function renderScoreboard(players) {
        const list = document.getElementById("reveal-scoreboard");
        list.innerHTML = "";
        players
            .slice()
            .sort((a, b) => b.score - a.score)
            .forEach(p => {
                const li = document.createElement("li");
                const resultTag = p.lastAnswerCorrect === true ? "✅" : (p.lastAnswerCorrect === false ? "❌" : "—");
                li.textContent = `${resultTag} ${p.displayName} — ${p.score}`;
                list.appendChild(li);
            });
    }
  
    function renderScoreboard(players) {
        const list = document.getElementById("reveal-scoreboard");
        list.innerHTML = "";
        players
            .slice()
            .sort((a, b) => b.score - a.score)
            .forEach(p => {
                const li = document.createElement("li");
                const resultTag = p.lastAnswerCorrect === true ? "✅" : (p.lastAnswerCorrect === false ? "❌" : "—");
                const penaltyTag = p.lastAnswerPenalized ? ` <span class="penalty-tag">(-50% own song)</span>` : "";
                li.innerHTML = `${resultTag} ${p.displayName} — ${p.score}${penaltyTag}`;
                list.appendChild(li);
            });
    }

    function leaveAndRedirect() {
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
                const icon = answer.wasCorrect ? "✅" : "❌";
                return `<div class="verdict-row"><span>${p.displayName}</span><span>${icon}</span></div>`;
            }).join("");

            const contributorLabel = buildContributorLabel(song.contributedBy);

            li.innerHTML = `
                <div class="song-recap-info">
                    <a href="${song.spotifyUrl}" target="_blank" class="song-recap-title">${song.songTitle}</a>
                    <span class="song-recap-artist">${song.artistName}</span>
                    ${contributorLabel}
                </div>
                <div class="song-recap-verdict">
                    <span class="my-verdict">${myVerdict}</span>
                    <div class="verdict-tooltip">${tooltipRows}</div>
                </div>
            `;
            list.appendChild(li);
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
});