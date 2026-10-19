(function () {
    const MIN_CHARS = 2;        // 2, not 3: Thai/CJK artist names can be very short
    const DEBOUNCE_MS = 300;
    const TIP = "Can't type the title? Try the Artist tab.";

    let root, fields, input, statusEl, resultsEl, tabsEl, artistBar, artistLabel;
    let onSubmit = null;
    let kind = "track";
    let pickedArtist = null;
    let locked = false;
    let resultShown = false;
    let timer = null;
    let seq = 0;
    let controller = null;

    function setStatus(text) { statusEl.textContent = text || ""; }
    function clearResults() { resultsEl.replaceChildren(); }
    function setLocked(v) { locked = v; fields.disabled = v; }

    function makeRow(imgUrl, primary, secondary, data) {
        const li = document.createElement("li");
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "search-result";
        Object.entries(data).forEach(([k, v]) => { btn.dataset[k] = String(v); });

        if (imgUrl) {
            const img = document.createElement("img");
            img.src = imgUrl;
            img.alt = "";
            img.width = 40;
            img.height = 40;
            img.loading = "lazy";
            btn.appendChild(img);
        }

        const text = document.createElement("span");
        text.className = "search-result-text";
        const p = document.createElement("span");
        p.className = "search-result-primary";
        p.textContent = primary;
        text.appendChild(p);
        if (secondary) {
            const s = document.createElement("span");
            s.className = "search-result-secondary";
            s.textContent = secondary;
            text.appendChild(s);
        }
        btn.appendChild(text);

        li.appendChild(btn);
        return li;
    }

    async function fetchJson(url) {
        if (controller) controller.abort();
        controller = new AbortController();
        const res = await fetch(url, { signal: controller.signal });
        if (!res.ok) throw new Error("search failed");
        return res.json();
    }

    function renderArtists(list) {
        clearResults();
        if (!list.length) { setStatus("No artists found."); return; }
        setStatus("");
        list.forEach(a => resultsEl.appendChild(
            makeRow(a.pictureUrl, a.name, "", { kind: "artist", artistId: a.artistId, name: a.name })));
    }

    function renderTracks(list) {
        clearResults();
        if (!list.length) { setStatus("No songs found."); return; }
        setStatus("");
        list.forEach(t => resultsEl.appendChild(
            makeRow(t.albumCoverUrl, t.title, t.artistName, { kind: "track", trackId: t.trackId })));
    }

    async function runSearch() {
        const q = input.value.trim();
        if (q.length < MIN_CHARS) {
            if (controller) controller.abort();
            clearResults();
            setStatus(kind === "track" ? TIP : "");
            return;
        }

        const mySeq = ++seq;
        setStatus("Searching…");
        try {
            const url = kind === "track"
                ? `/api/deezer/tracks?q=${encodeURIComponent(q)}`
                : `/api/deezer/artists?q=${encodeURIComponent(q)}`;
            const data = await fetchJson(url);
            if (mySeq !== seq) return;
            (kind === "track" ? renderTracks : renderArtists)(data);
        } catch (err) {
            if (err.name === "AbortError" || mySeq !== seq) return;
            clearResults();
            setStatus("Search is unavailable right now. Try again in a moment.");
        }
    }

    async function browseArtist(artistId, name) {
        pickedArtist = { artistId, name };
        artistLabel.textContent = name;
        artistBar.classList.remove("d-none");
        input.classList.add("d-none");
        tabsEl.classList.add("d-none");
        clearResults();
        setStatus("Loading songs…");

        const mySeq = ++seq;
        try {
            const data = await fetchJson(`/api/deezer/artists/${encodeURIComponent(artistId)}/top`);
            if (mySeq !== seq) return;
            renderTracks(data);
            if (!data.length) setStatus("No songs found. You can still answer with the artist only.");
        } catch (err) {
            if (err.name === "AbortError" || mySeq !== seq) return;
            clearResults();
            setStatus("Couldn't load songs. You can still answer with the artist only.");
        }
    }

    function leaveArtist() {
        pickedArtist = null;
        artistBar.classList.add("d-none");
        input.classList.remove("d-none");
        tabsEl.classList.remove("d-none");
        runSearch();
    }

    function setKind(next) {
        kind = next;
        tabsEl.querySelectorAll(".search-tab").forEach(b =>
            b.classList.toggle("active", b.dataset.kind === next));
        input.placeholder = next === "track" ? "Search for the song…" : "Search for the artist…";
        runSearch();
    }

    async function submit(payload) {
        if (locked || !onSubmit) return;
        setLocked(true);
        setStatus("Submitting…");

        let ok = false;
        try { ok = await onSubmit(payload); } catch { ok = false; }

        if (ok) {
            if (!resultShown) setStatus("Answer locked in");
        } else {
            setLocked(false);
            setStatus("Couldn't submit that answer. Try again.");
        }
    }

    function init(cfg) {
        root = document.getElementById("search-answer");
        fields = document.getElementById("search-answer-fields");
        input = document.getElementById("search-answer-input");
        statusEl = document.getElementById("search-answer-status");
        resultsEl = document.getElementById("search-answer-results");
        tabsEl = document.getElementById("search-answer-tabs");
        artistBar = document.getElementById("search-answer-artist-bar");
        artistLabel = document.getElementById("search-answer-artist-label");
        onSubmit = cfg.onSubmit;

        tabsEl.addEventListener("click", e => {
            const b = e.target.closest(".search-tab");
            if (b && b.dataset.kind !== kind) setKind(b.dataset.kind);
        });

        input.addEventListener("input", () => {
            clearTimeout(timer);
            timer = setTimeout(runSearch, DEBOUNCE_MS);
        });

        resultsEl.addEventListener("click", e => {
            const btn = e.target.closest(".search-result");
            if (!btn || locked) return;
            if (btn.dataset.kind === "artist") {
                browseArtist(Number(btn.dataset.artistId), btn.dataset.name);
            } else {
                submit({ trackId: Number(btn.dataset.trackId), artistId: null });
            }
        });

        document.getElementById("search-answer-artist-submit").addEventListener("click", () => {
            if (pickedArtist) submit({ trackId: null, artistId: pickedArtist.artistId });
        });
        document.getElementById("search-answer-artist-back").addEventListener("click", leaveArtist);
    }

    function reset() {
        clearTimeout(timer);
        if (controller) controller.abort();
        seq++;
        pickedArtist = null;
        resultShown = false;
        kind = "track";
        tabsEl.querySelectorAll(".search-tab").forEach(b =>
            b.classList.toggle("active", b.dataset.kind === "track"));
        artistBar.classList.add("d-none");
        input.classList.remove("d-none");
        tabsEl.classList.remove("d-none");
        input.value = "";
        input.placeholder = "Search for the song…";
        clearResults();
        setLocked(false);
        setStatus(TIP);
    }

    function show(canAnswer) {
        root.classList.remove("d-none");
        if (canAnswer === false) { setLocked(true); setStatus("Answer locked in"); }
    }

    function hide() { if (root) root.classList.add("d-none"); }

    function showResult(data) {
        resultShown = true;
        setLocked(true);
        if (data.wasCorrect) setStatus(`✅ Correct! +${data.awardedScore}`);
        else if (data.wasArtistCorrect) setStatus(`🎤 Right artist, wrong song. +${data.awardedScore}`);
        else setStatus("❌ Not quite");
    }

    window.SearchAnswerPicker = { init, reset, show, hide, showResult };
})();