document.addEventListener("DOMContentLoaded", () => {
    document.getElementById("save-name-btn").addEventListener("click", async () => {
        const input = document.getElementById("display-name-input");
        const status = document.getElementById("name-save-status");
        const heroName = document.getElementById("hero-display-name");
        const name = input.value.trim();

        if (!name) return;

        try {
            const res = await fetch("/dashboard/set-display-name", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ displayName: name })
            });

            if (res.ok) {
                status.textContent = "Saved ✓";
                status.style.color = "#1DB954";
                if (heroName) heroName.textContent = name;
            } else {
                status.textContent = "Couldn't save — try a shorter name.";
                status.style.color = "#f15e6c";
            }
        } catch {
            status.textContent = "Network error.";
            status.style.color = "#f15e6c";
        } finally {
            setTimeout(() => { status.textContent = ""; }, 2500);
        }
    });
});