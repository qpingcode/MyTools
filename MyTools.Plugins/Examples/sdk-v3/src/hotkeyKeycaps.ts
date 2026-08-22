/** Render a host-formatted shortcut as the same sequence of keycaps used by the desktop action bar. */
export function renderHotkeyKeycaps(element: HTMLElement, hotkey: string): void {
  const normalized = hotkey.trim();
  element.hidden = normalized.length === 0;
  element.setAttribute("aria-label", normalized);
  element.classList.add("hotkey-keycaps");

  const keycaps = normalized.length === 0
    ? []
    : normalized.split("+").map((token) => createKeycap(token.trim()));
  element.replaceChildren(...keycaps);
}

function createKeycap(token: string): HTMLSpanElement {
  const keycap = document.createElement("span");
  keycap.className = "hotkey-keycap";
  keycap.setAttribute("aria-hidden", "true");

  if (token.toLowerCase() === "enter" || token.toLowerCase() === "return") {
    keycap.classList.add("hotkey-keycap-enter");
    keycap.textContent = "↵";
    keycap.title = "Enter";
  } else {
    keycap.textContent = token;
  }

  return keycap;
}
