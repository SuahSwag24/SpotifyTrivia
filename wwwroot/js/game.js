document.addEventListener("DOMContentLoaded", () => {
    const container = document.querySelector(".game-container");
    const lobbyCode = container.dataset.lobbyCode;
    const playerId = container.dataset.playerId;

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
            runLocalCountdown(data.seconds);
        },
        onRoundStarted: (data) => {
            showPhase("question-phase");
            document.getElementById("album-cover").src = data.albumCoverUrl;

            audio.src = data.previewUrl;
            audio.currentTime = 0;
            audio.play().catch(() => console.log("Autoplay blocked — user interaction required."));

            renderAnswerChoices(data.answerChoices);
            runRoundTimer(data.durationSeconds);
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
        onLobbyDisbanded: () => { window.location.href = "/multiplayer"; }
    });

    connection.start()
        .then(() => connection.invoke("JoinLobby", lobbyCode, playerId, "Player"))
        .catch(err => console.error(err));

    function renderAnswerChoices(choices) {
        const container = document.getElementById("answer-choices");
        container.innerHTML = "";
        choices.forEach((choice, index) => {
            const btn = document.createElement("button");
            btn.textContent = choice;
            btn.addEventListener("click", () => {
                container.querySelectorAll("button").forEach(b => b.disabled = true);
                btn.classList.add("selected-answer");
                connection.invoke("SubmitAnswer", lobbyCode, playerId, index)
                    .catch(err => console.error("Answer submit failed:", err));
            });
            container.appendChild(btn);
        });
    }

    function runLocalCountdown(seconds) {
        const el = document.getElementById("countdown-number");
        let remaining = seconds;
        el.textContent = remaining;
        const interval = setInterval(() => {
            remaining--;
            el.textContent = remaining;
            if (remaining <= 0) clearInterval(interval);
        }, 1000);
    }

    function runRoundTimer(seconds) {
        const el = document.getElementById("round-timer");
        let remaining = seconds;
        el.textContent = remaining;
        const interval = setInterval(() => {
            remaining--;
            el.textContent = remaining;
            if (remaining <= 0) clearInterval(interval);
        }, 1000);
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
});