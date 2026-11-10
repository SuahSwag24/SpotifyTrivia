document.addEventListener("DOMContentLoaded", () => {
    const playlistPicker = document.getElementById("singleplayer-playlist-modal");
    const settingsModal = document.getElementById("singleplayer-settings-modal");

    if (!playlistPicker || !settingsModal) return;

    const selectedPlaylistId = document.getElementById("selected-playlist-id");
    const selectedPlaylistName = document.getElementById("selected-playlist-name");
    const selectedPlaylistImage = document.getElementById("selected-playlist-image");
    const selectedPlaylistPlaceholder = document.getElementById("selected-playlist-placeholder");

    playlistPicker.addEventListener("click", event => {
        const button = event.target.closest(".playlist-select-btn");
        if (!button) return;

        selectedPlaylistId.value = button.dataset.playlistId;
        selectedPlaylistName.textContent = button.dataset.playlistName;

        if (button.dataset.playlistImage) {
            selectedPlaylistImage.src = button.dataset.playlistImage;
            selectedPlaylistImage.classList.remove("d-none");
            selectedPlaylistPlaceholder.classList.add("d-none");
        } else {
            selectedPlaylistImage.removeAttribute("src");
            selectedPlaylistImage.classList.add("d-none");
            selectedPlaylistPlaceholder.classList.remove("d-none");
        }

        bootstrap.Modal.getOrCreateInstance(playlistPicker).hide();
    });

    const questionSlider = document.getElementById("sp-question-count-slider");
    const questionDisplay = document.getElementById("sp-question-count-display");
    const roundDurationSlider = document.getElementById("sp-round-duration-slider");
    const roundDurationDisplay = document.getElementById("sp-round-duration-display");
    const saveSettingsButton = document.getElementById("sp-save-settings-btn");
    const backSettingsButton = document.getElementById("sp-back-settings-btn");
    const settingsSummary = document.getElementById("settings-summary");

    const savedSettings = {
        questionCount: document.getElementById("saved-question-count"),
        roundDuration: document.getElementById("saved-round-duration"),
        sampleSize: document.getElementById("saved-sample-size"),
        blurAlbum: document.getElementById("saved-blur-album"),
        answerStyle: document.getElementById("saved-answer-style")
    };

    const selectedMode = () => document.querySelector('input[name="gameMode"]:checked')?.value ?? null;
    const sampleSizeInput = value => document.querySelector(`input[name="sp-sample-size-step"][value="${value}"]`);
    const blurAlbumInput = value => document.querySelector(`input[name="sp-blur-album"][value="${value}"]`);
    const answerStyleInput = value => document.querySelector(`input[name="sp-answer-style"][value="${value}"]`);

    function syncSliderLabels() {
        questionDisplay.textContent = questionSlider.value;
        roundDurationDisplay.textContent = `${roundDurationSlider.value}s`;
    }

    function syncAnswerStyleAvailability() {
        const searchInput = answerStyleInput("Search");
        const answerStyleHint = document.getElementById("sp-answer-style-hint");
        const isClassicMode = selectedMode() === "ClassicGuessSong";

        searchInput.disabled = !isClassicMode;
        if (!isClassicMode && answerStyleInput("MultipleChoice")) {
            answerStyleInput("MultipleChoice").checked = true;
            savedSettings.answerStyle.value = "MultipleChoice";
        }

        answerStyleHint.textContent = isClassicMode
            ? "Choose from multiple-choice answers or use hardmode."
            : "Hardmode is only available for Classic Trivia.";
    }

    function restoreSavedSettings() {
        questionSlider.value = savedSettings.questionCount.value;
        roundDurationSlider.value = savedSettings.roundDuration.value;
        sampleSizeInput(savedSettings.sampleSize.value).checked = true;
        blurAlbumInput(savedSettings.blurAlbum.value).checked = true;
        answerStyleInput(savedSettings.answerStyle.value).checked = true;
        syncSliderLabels();
        syncAnswerStyleAvailability();
    }

    questionSlider.addEventListener("input", syncSliderLabels);
    roundDurationSlider.addEventListener("input", syncSliderLabels);
    settingsModal.addEventListener("show.bs.modal", restoreSavedSettings);

    document.querySelectorAll('input[name="gameMode"]').forEach(input => {
        input.addEventListener("change", syncAnswerStyleAvailability);
    });

    backSettingsButton.addEventListener("click", () => {
        restoreSavedSettings();
        bootstrap.Modal.getOrCreateInstance(settingsModal).hide();
    });

    saveSettingsButton.addEventListener("click", () => {
        savedSettings.questionCount.value = questionSlider.value;
        savedSettings.roundDuration.value = roundDurationSlider.value;
        savedSettings.sampleSize.value = document.querySelector('input[name="sp-sample-size-step"]:checked').value;
        savedSettings.blurAlbum.value = document.querySelector('input[name="sp-blur-album"]:checked').value;
        savedSettings.answerStyle.value = document.querySelector('input[name="sp-answer-style"]:checked').value;

        const sampleSizeLabel = savedSettings.sampleSize.value === "max"
            ? "all tracks"
            : `${savedSettings.sampleSize.value} track sample`;
        settingsSummary.textContent =
            `${savedSettings.questionCount.value} questions · ${savedSettings.roundDuration.value} seconds per round · ${sampleSizeLabel}`;

        bootstrap.Modal.getOrCreateInstance(settingsModal).hide();
    });

    syncSliderLabels();
    syncAnswerStyleAvailability();
});