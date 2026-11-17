document.addEventListener("DOMContentLoaded", () => {
    const container = document.querySelector(".game-container");
    const sessionId = container.dataset.sessionId;
    const displayName = container.dataset.displayName;
    const totalQuestions = parseInt(container.dataset.totalQuestions, 10);
    const countdownSeconds = parseInt(container.dataset.countdownSeconds, 10);
    const revealSeconds = parseInt(container.dataset.revealSeconds, 10);
    const audioTimeoutSeconds = parseInt(container.dataset.audioTimeoutSeconds, 10);
    const startingScore = parseInt(container.dataset.startingScore, 10) || 0;
    const baseUrl = `/singleplayer/${encodeURIComponent(sessionId)}`;

    const $ = id => document.getElementById(id);
    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
    const esc = value => String(value ?? "").replace(/[&<>"']/g, c =>
        ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    const toast = (message, type) =>
        typeof showToast === "function" ? showToast(message, type) : console.warn(message);

    const albumCover = $("album-cover");
    const revealAlbumCover = $("reveal-album-cover");
    const skipRoundButton = $("skip-round-btn");

    let round = null;          // { number, answered, closed, finishEarly }
    let runningScore = startingScore;

    skipRoundButton.addEventListener("click", () => {
        if (!round || round.closed) return;
        skipRoundButton.disabled = true;
        round.finishEarly();
    });

    async function api(method, path, body) {
        const res = await fetch(`${baseUrl}${path}`, {
            method,
            headers: body ? { "Content-Type": "application/json" } : undefined,
            body: body ? JSON.stringify(body) : undefined
        });
        let data = null;
        try { data = await res.json(); } catch { /* no body */ }
        if (!res.ok) {
            const err = new Error(data?.error ?? `Request failed (${res.status})`);
            err.status = res.status;
            throw err;
        }
        return data;
    }

    function showError(message) {
        $("game-error-text").textContent = message;
        $("game-error").style.display = "block";
    }

    function showPhase(id) {
        document.querySelectorAll(".phase-panel").forEach(p => p.style.display = "none");
        $(id).style.display = "block";
    }

    // ---------- Audio ----------
    let activeAudio = $("round-audio");
    let nextAudio = $("next-round-audio");
    const enableAudioButton = $("enable-audio-btn");
    let pendingAudio = null;

    const volumeSlider = $("volume-slider");
    const volumeIcon = $("volume-icon");
    const savedVolume = localStorage.getItem("triviaVolume");
    const initialVolume = savedVolume !== null ? parseInt(savedVolume, 10) : 30;

    function applyVolume(value) {
        activeAudio.volume = value / 100;
        nextAudio.volume = value / 100;
        volumeIcon.textContent = value === 0 ? "🔇" : value < 50 ? "🔉" : "🔊";
    }
    volumeSlider.value = initialVolume;
    applyVolume(initialVolume);
    volumeSlider.addEventListener("input", () => {
        const value = parseInt(volumeSlider.value, 10);
        applyVolume(value);
        localStorage.setItem("triviaVolume", value);
    });

    enableAudioButton.addEventListener("click", async () => {
        if (!pendingAudio) return;
        try {
            await pendingAudio.play();
            pendingAudio = null;
            enableAudioButton.classList.add("d-none");
        } catch (err) {
            console.warn("Audio could not be enabled.", err);
        }
    });

    // Loads the round's prompt and preloads its audio into the idle element
    async function loadRound(n) {
        const prep = await api("GET", `/round/${n}/prepare`);

        await new Promise(resolve => {
            const timer = setTimeout(resolve, audioTimeoutSeconds * 1000);
            const done = () => { clearTimeout(timer); resolve(); };
            nextAudio.addEventListener("canplaythrough", done, { once: true });
            nextAudio.addEventListener("error", done, { once: true });
            nextAudio.pause();
            nextAudio.src = prep.previewUrl;
            nextAudio.load();
        });

        return prep;
    }

    async function startRoundAudio() {
        [activeAudio, nextAudio] = [nextAudio, activeAudio];
        nextAudio.pause();
        enableAudioButton.classList.add("d-none");
        pendingAudio = null;

        try {
            activeAudio.currentTime = 0;
            await activeAudio.play();
        } catch (err) {
            pendingAudio = activeAudio;
            enableAudioButton.classList.remove("d-none");
            console.warn("Autoplay blocked; waiting for user interaction.", err);
        }
    }

    // ---------- Timers ----------
    function runCountdown(seconds) {
        return new Promise(resolve => {
            const el = $("countdown-number");
            const end = Date.now() + seconds * 1000;
            let interval;
            const tick = () => {
                const remaining = Math.max(0, Math.ceil((end - Date.now()) / 1000));
                el.textContent = remaining;
                if (remaining <= 0) {
                    clearInterval(interval);
                    resolve();
                }
            };
            interval = setInterval(tick, 200);
            tick();
        });
    }

    function startRoundTimer(totalSeconds) {
        const el = $("round-timer");
        const end = Date.now() + totalSeconds * 1000;
        const tick = () => { el.textContent = Math.max(0, Math.ceil((end - Date.now()) / 1000)); };
        tick();
        const interval = setInterval(tick, 250);
        return () => clearInterval(interval);
    }

    // ---------- Question phase ----------
    SearchAnswerPicker.init({
        artistOnly: container.dataset.gameMode === "GuessArtist",
        onSubmit: ({ trackId, artistId }) => submitAnswer({ trackId, artistId }, null)
    });

    function renderAnswerChoices(choices) {
        const choicesEl = $("answer-choices");
        choicesEl.innerHTML = "";
        choices.forEach((text, index) => {
            const btn = document.createElement("button");
            btn.textContent = text;
            btn.className = "answer-btn";
            btn.dataset.index = index;
            btn.addEventListener("click", () => handleAnswerSelected(index, btn));
            choicesEl.appendChild(btn);
        });
    }

    async function handleAnswerSelected(index, btn) {
        const buttons = [...document.querySelectorAll("#answer-choices .answer-btn")];
        buttons.forEach(b => b.disabled = true);
        btn.classList.add("selected");

        const ok = await submitAnswer({ choiceIndex: index }, btn);
        if (!ok && round && !round.answered && !round.closed) {
            buttons.forEach(b => b.disabled = false);
            btn.classList.remove("selected");
        }
    }

    // Returns true when the server accepted the answer (SearchAnswerPicker relies on this)
    async function submitAnswer(body, button) {
        const current = round;
        if (!current || current.answered || current.closed) return false;

        try {
            const result = await api("POST", `/round/${current.number}/answer`, body);
            current.answered = true;
            if (button) button.classList.add(result.wasCorrect ? "correct" : "incorrect");
            skipRoundButton.classList.remove("d-none");
            return true;
        } catch (err) {
            toast(err.message, "warning");
            return false;
        }
    }

    function renderRound(data) {
        showPhase("question-phase");
        skipRoundButton.disabled = false;
        skipRoundButton.classList.add("d-none");

        const visibility = (data.blurAlbum ?? "hide").toString().toLowerCase();
        const placeholder = $("album-cover-hidden-placeholder");

        albumCover.src = data.albumCoverUrl;
        albumCover.style.display = "block";
        albumCover.classList.remove("album-blurred");
        if (placeholder) placeholder.style.display = "none";

        if (visibility === "hide") {
            albumCover.style.display = "none";
            if (placeholder) placeholder.style.display = "flex";
        } else if (visibility === "blur") {
            albumCover.classList.add("album-blurred");
        }

        $("round-counter").textContent = `Question ${data.questionNumber}/${data.totalQuestions}`;
        $("progress-fill").style.width =
            Math.min(100, Math.round(((data.questionNumber - 1) / data.totalQuestions) * 100)) + "%";

        const choicesEl = $("answer-choices");
        if (data.answerStyle === "Search") {
            choicesEl.replaceChildren();
            choicesEl.classList.add("d-none");
            SearchAnswerPicker.reset();
            SearchAnswerPicker.show(true);
        } else {
            choicesEl.classList.remove("d-none");
            SearchAnswerPicker.hide();
            renderAnswerChoices(data.answerChoices);
        }
    }

    async function playRound(n, data) {
        let finishEarly;
        const early = new Promise(resolve => { finishEarly = resolve; });
        round = { number: n, answered: false, closed: false, finishEarly };

        renderRound(data);
        startRoundAudio();                       // not awaited: handles its own autoplay fallback
        const stopTimer = startRoundTimer(data.durationSeconds);

        await Promise.race([sleep(data.durationSeconds * 1000), early]);

        stopTimer();
        round.closed = true;
        skipRoundButton.disabled = true;
        // Let an answer that was in flight at the deadline land before ending the round
        if (!round.answered) await sleep(400);
    }

    async function endRound(n) {
        for (let attempt = 0; attempt < 5; attempt++) {
            try {
                return await api("POST", `/round/${n}/end`);
            } catch (err) {
                if (err.status !== 409) throw err;
                await sleep(500);
            }
        }
        throw new Error("Couldn't finish the round.");
    }

    // ---------- Reveal phase ----------
    function animateScoreCount(el, from, to, duration = 600) {
        const start = performance.now();
        function tick(now) {
            const progress = Math.min((now - start) / duration, 1);
            const eased = 1 - Math.pow(1 - progress, 3);
            el.textContent = Math.round(from + (to - from) * eased);
            if (progress < 1) requestAnimationFrame(tick);
        }
        requestAnimationFrame(tick);
    }

    function renderReveal(data, answered) {
        showPhase("reveal-phase");

        revealAlbumCover.classList.remove("reveal-animation");
        revealAlbumCover.src = data.albumCoverUrl;
        const startAnimation = () => {
            void revealAlbumCover.offsetWidth;
            revealAlbumCover.classList.add("reveal-animation");
        };
        if (revealAlbumCover.complete) startAnimation();
        else revealAlbumCover.onload = startAnimation;

        $("correct-answer-label").textContent = `Correct answer: ${data.correctAnswer}`;

        const p = data.players[0];
        const prev = runningScore;
        const delta = p.score - prev;
        const artistOnly = p.lastAnswerCorrect === false && p.lastAnswerArtistCorrect === true;

        let stateClass = "state-noanswer";
        let tag = "—";
        if (answered) {
            if (p.lastAnswerCorrect === true) { stateClass = "state-correct"; tag = "✅"; }
            else if (artistOnly) { stateClass = "state-artist"; tag = "🎤"; }
            else { stateClass = "state-incorrect"; tag = "❌"; }
        }

        const list = $("reveal-scoreboard");
        list.innerHTML = "";
        const li = document.createElement("li");
        li.className = `reveal-row is-current-player ${stateClass}`;
        li.innerHTML = `
            <span class="row-left">${tag} ${esc(p.displayName)}</span>
            <span class="row-right">
                <span class="score-value">${prev}</span>${delta > 0 ? ` <span class="score-delta">+${delta}</span>` : ""}${artistOnly ? ' <span class="penalty-tag">(-60% artist only)</span>' : ""}
            </span>`;
        list.appendChild(li);

        setTimeout(() => {
            const scoreEl = li.querySelector(".score-value");
            if (scoreEl && prev !== p.score) animateScoreCount(scoreEl, prev, p.score);
        }, 500);

        runningScore = p.score;
    }

    // ---------- Finished phase ----------
    let currentlyPlayingBtn = null;

    function handleRecapPlay(btn) {
        const url = btn.dataset.previewUrl;

        if (currentlyPlayingBtn === btn) {
            if (activeAudio.paused) {
                if (activeAudio.ended) activeAudio.currentTime = 0;
                activeAudio.play();
                btn.textContent = "Pause";
            } else {
                activeAudio.pause();
                btn.textContent = "Play";
            }
            return;
        }

        if (currentlyPlayingBtn) currentlyPlayingBtn.textContent = "Play";

        activeAudio.src = url;
        activeAudio.currentTime = 0;
        activeAudio.play()
            .then(() => { btn.textContent = "Pause"; currentlyPlayingBtn = btn; })
            .catch(() => {
                toast("This preview is no longer available.", "warning");
                btn.disabled = true;
                btn.textContent = "Unavailable";
                currentlyPlayingBtn = null;
            });
    }

    async function showFinished() {
        const summary = await api("GET", "/summary");
        showPhase("finished-phase");

        $("final-summary").textContent = `${summary.correctCount} of ${summary.totalQuestions} correct`;

        const leaderboard = $("final-leaderboard");
        leaderboard.innerHTML = "";
        const row = document.createElement("li");
        row.textContent = `${displayName} — ${summary.score}`;
        leaderboard.appendChild(row);

        const list = $("song-list-board");
        list.innerHTML = "";

        summary.rounds.forEach((r, i) => {
            const result = r.result;
            const verdict = result.wasCorrect ? "✅" : (result.wasArtistCorrect ? "🎤" : "❌");
            const points = result.awardedScore > 0 ? ` <span class="score-delta">+${result.awardedScore}</span>` : "";
            const playButton = r.previewUrl
                ? `<button class="recap-play-btn" data-preview-url="${esc(r.previewUrl)}" data-song-index="${i}">Play</button>`
                : "";

            const li = document.createElement("li");
            li.className = "song-recap-item";
            li.innerHTML = `
                <div class="song-recap-info">
                    ${playButton}
                    <div class="song-recap-text">
                        <a href="${esc(r.spotifyUrl)}" target="_blank" rel="noopener" class="song-recap-title">${esc(r.songTitle)}</a>
                        <span class="song-recap-artist">${esc(r.artistName)}</span>
                    </div>
                </div>
                <div class="song-recap-verdict">
                    <span class="my-verdict">${verdict}</span>${points}
                </div>`;
            list.appendChild(li);
        });

        list.querySelectorAll(".recap-play-btn").forEach(btn =>
            btn.addEventListener("click", () => handleRecapPlay(btn)));
    }

    // ---------- Leave ----------
    const leaveModal = bootstrap.Modal.getOrCreateInstance($("leave-confirmation-modal"));
    $("leave-game-btn").addEventListener("click", () => leaveModal.show());
    $("confirm-leave-btn").addEventListener("click", () => { window.location.href = "/singleplayer"; });
    $("continue-game-btn").addEventListener("click", async event => {
        const button = event.currentTarget;
        button.disabled = true;
        button.textContent = "Preparing game...";

        try {
            const result = await api("POST", "/continue");
            window.location.href = `/singleplayer/game/${encodeURIComponent(result.sessionId)}`;
        } catch (err) {
            toast(err.message, "warning");
            button.disabled = false;
            button.textContent = "Continue Playing";
        }
    });

    // ---------- Main loop ----------
    function showPreparing() {
        showPhase("countdown-phase");
        $("audio-loading-container").classList.remove("d-none");
        $("audio-loading-container").classList.add("d-flex");
        $("countdown-number").style.display = "none";
    }

    async function run() {
        let pending = loadRound(1);
        pending.catch(() => { });

        for (let n = 1; n <= totalQuestions; n++) {
            showPreparing();
            const prep = await pending;

            $("prompt-text").textContent = prep.prompt;
            $("audio-loading-container").classList.remove("d-flex");
            $("audio-loading-container").classList.add("d-none");
            $("countdown-number").style.display = "block";
            await runCountdown(countdownSeconds);

            const data = await api("POST", `/round/${n}/begin`);
            await playRound(n, data);

            const answered = round.answered;
            const result = await endRound(n);
            activeAudio.pause();

            // Preload the next round while the reveal is on screen
            if (n < totalQuestions) {
                pending = loadRound(n + 1);
                pending.catch(() => { });
            }

            renderReveal(result, answered);
            await sleep(revealSeconds * 1000);
        }

        await showFinished();
    }

    run().catch(err => {
        console.error(err);
        showError(err.status === 409
            ? "This game is out of sync (the page may have been refreshed mid-game)."
            : err.message);
    });
});