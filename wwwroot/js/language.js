// Razor owns UI copy. Never rewrite arbitrary DOM text, log values or user data.
(function () {
    function apply(code) {
        const language = code === "ko" ? "ko" : "en";
        document.documentElement.lang = language;
        // Connection messages live outside the interactive circuit, so use explicit pairs.
        document.querySelectorAll("[data-ui-ko][data-ui-en]").forEach(element => {
            element.textContent = element.getAttribute("data-ui-" + language);
        });
        window.dispatchEvent(new CustomEvent("ops-language-changed", { detail: { language } }));
    }
    window.opsLanguage = { apply };
})();
