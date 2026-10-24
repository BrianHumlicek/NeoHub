window.neoHubTheme = {
    getPreference: function () {
        try {
            return localStorage.getItem("neohub-theme") || "system";
        } catch {
            return "system";
        }
    },

    setPreference: function (preference) {
        try {
            localStorage.setItem("neohub-theme", preference);
        } catch {
            // Browser storage may be unavailable.
        }
    },

    systemPrefersDark: function () {
        return window.matchMedia("(prefers-color-scheme: dark)").matches;
    }
};
