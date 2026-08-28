document.addEventListener("DOMContentLoaded", () => {
    const container = document.querySelector(".game-container");
    const lobbyCode = container.dataset.lobbyCode;
    const playerId = container.dataset.playerId;
    const displayName = container.dataset.displayName;

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
        },
        onRoundStarted: (data) => {
            showPhase("question-phase");

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
        onGameEnded: (data) => {
            showPhase("finished-phase");
            renderLeaderboard(data);
        },
        onActionError: (data) => showError(data.message),
        onLobbyDisbanded: () => { window.location.href = "/multiplayer"; },
        onPlayerLeft: (data) => {
            showToast(`${data.displayName} left the game`, "warning");
        },
        onPlayerJoined: (data) => {
            showToast(`${data.displayName} joined the game`, "success");
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

    function leaveAndRedirect() {
        connection.invoke("LeaveLobby", lobbyCode, playerId)
            .catch(err => console.error("Leave failed:", err))
            .finally(() => { window.location.href = "/multiplayer" });
    }
    document.getElementById("leave-game-btn")?.addEventListener("click", leaveAndRedirect);
    document.getElementById("leave-results-btn")?.addEventListener("click", leaveAndRedirect);
});