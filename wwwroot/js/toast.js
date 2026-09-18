function showToast(message, toastType = "warning") {
    let stack = document.getElementById("toast-stack");

    if (!stack) {
        stack = document.createElement("div");
        stack.id = "toast-stack";
        stack.style.cssText = "position: fixed; bottom: 20px; right: 20px; display: flex; flex-direction: column; gap: 0.5rem; z-index: 1050;";
        document.body.appendChild(stack);
    }

    const toast = document.createElement("div");
    toast.textContent = message;
    toast.classList.add("alert", `alert-${toastType}`);
    toast.style.opacity = "0";
    toast.style.transform = "translateY(10px)";
    toast.style.transition = "opacity 0.25s ease, transform 0.25s ease";

    stack.appendChild(toast);

    requestAnimationFrame(() => {
        toast.style.opacity = "1";
        toast.style.transform = "translateY(0)";
    });

    setTimeout(() => {
        toast.style.opacity = "0";
        toast.style.transform = "translateY(10px)";
        setTimeout(() => toast.remove(), 250);
    }, 3000);
}