/**
 * Internationalization (i18n) Module
 * 
 * Loads translated strings from locale JSON files and applies them to the UI.
 * The active language is determined by the server's UI_LANGUAGE setting,
 * communicated via the /api/config endpoint.
 * 
 * Usage in HTML:
 *   <span data-i18n="sim.cpu.title">CPU Stress</span>
 *   <span data-i18n-tooltip="sim.cpu.tooltip">...</span>
 *   <span data-i18n-placeholder="sim.cpu.duration">...</span>
 *   <span data-i18n-title="sim.cpu.duration">...</span>
 * 
 * Usage in JS:
 *   i18n('log.cpu.triggering', { duration: 30, level: 'high' })
 *   // Returns: "Triggering CPU stress for 30 seconds (high)..."
 */

const I18N = {
    currentLanguage: 'en',
    strings: {},
    loaded: false,

    /**
     * Initialize i18n by loading the active locale.
     * Called after fetchAppConfig() sets the language.
     * @param {string} language - ISO 639-1 language code
     */
    async init(language) {
        this.currentLanguage = language || 'en';

        if (this.currentLanguage === 'en') {
            // English is the source — load en.json as the base
            try {
                const response = await fetch(`/locales/en.json?v=${Date.now()}`);
                if (response.ok) {
                    this.strings = await response.json();
                }
            } catch (err) {
                console.warn('Failed to load en.json, using inline defaults', err);
            }
            this.loaded = true;
            this.applyToDOM();
            return;
        }

        // Try to load the translated locale file
        try {
            const response = await fetch(`/locales/${this.currentLanguage}.json?v=${Date.now()}`);
            if (response.ok) {
                this.strings = await response.json();
                this.loaded = true;
                console.log(`Loaded locale: ${this.currentLanguage}`);
            } else {
                console.warn(`Locale ${this.currentLanguage}.json not found (${response.status}), falling back to English`);
                this.currentLanguage = 'en';
                await this.init('en');
                return;
            }
        } catch (err) {
            console.warn(`Failed to load locale ${this.currentLanguage}, falling back to English`, err);
            this.currentLanguage = 'en';
            await this.init('en');
            return;
        }

        this.applyToDOM();
    },

    /**
     * Get a translated string by key, with optional placeholder substitution.
     * Placeholders use {name} syntax.
     * @param {string} key - The translation key (e.g., 'log.cpu.triggering')
     * @param {Object} [params] - Key/value pairs for placeholder substitution
     * @returns {string} The translated string, or the key itself if not found
     */
    t(key, params) {
        let text = this.strings[key];
        if (text === undefined) {
            // Key not found — return the key itself as fallback
            return key;
        }

        if (params) {
            for (const [k, v] of Object.entries(params)) {
                text = text.replaceAll(`{${k}}`, v);
            }
        }

        return text;
    },

    /**
     * Apply translations to all DOM elements with data-i18n attributes.
     * Called once after locale loads and can be called again after dynamic content.
     */
    applyToDOM() {
        if (!this.loaded) return;

        // data-i18n: replace textContent
        document.querySelectorAll('[data-i18n]').forEach(el => {
            const key = el.getAttribute('data-i18n');
            const text = this.strings[key];
            if (text !== undefined) {
                el.textContent = text;
            }
        });

        // data-i18n-html: replace innerHTML (for content with markup)
        document.querySelectorAll('[data-i18n-html]').forEach(el => {
            const key = el.getAttribute('data-i18n-html');
            const text = this.strings[key];
            if (text !== undefined) {
                el.innerHTML = text;
            }
        });

        // data-i18n-tooltip: replace data-tooltip attribute
        document.querySelectorAll('[data-i18n-tooltip]').forEach(el => {
            const key = el.getAttribute('data-i18n-tooltip');
            const text = this.strings[key];
            if (text !== undefined) {
                el.setAttribute('data-tooltip', text);
            }
        });

        // data-i18n-title: replace title attribute
        document.querySelectorAll('[data-i18n-title]').forEach(el => {
            const key = el.getAttribute('data-i18n-title');
            const text = this.strings[key];
            if (text !== undefined) {
                el.setAttribute('title', text);
            }
        });

        // data-i18n-placeholder: replace placeholder attribute
        document.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
            const key = el.getAttribute('data-i18n-placeholder');
            const text = this.strings[key];
            if (text !== undefined) {
                el.setAttribute('placeholder', text);
            }
        });

        // Update page title
        const pageTitle = this.strings['page.title'];
        if (pageTitle) {
            document.title = pageTitle;
        }
    }
};

/**
 * Convenience function — shorthand for I18N.t()
 * @param {string} key - Translation key
 * @param {Object} [params] - Placeholder substitution params
 * @returns {string} Translated string
 */
function i18n(key, params) {
    return I18N.t(key, params);
}
