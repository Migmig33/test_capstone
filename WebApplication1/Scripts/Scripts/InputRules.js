/*
 * Shared input rules for every AngularJS form.
 *
 * Markup:
 *   <input type="text" ng-model="x" input-rule="name" input-rule-label="Full name" input-rule-required>
 *
 * While typing, pasting, dropping or autofilling, characters a rule does not allow are
 * removed and the caret stays where it was. Input that would go over a length or range
 * limit is rejected (the field keeps its last valid value) instead of being truncated.
 * On blur the value is trimmed and validated, and the message is shown under the field.
 *
 * Controllers call InputRules.collect($scope) before saving. That runs the same check on
 * every bound field and returns the messages. InputRules.validate(rule, value, opts) can
 * be used directly for values that are not bound to an input.
 *
 * The rules are enforced again on the server in Helper/InputValidator.cs.
 */
app.factory('InputRules', function () {

    function str(value) {
        return value === null || value === undefined ? '' : String(value);
    }

    // Collapse runs of spaces to one and drop leading spaces. A single trailing space is
    // kept while typing so that multi-word names can be typed; blur trims it.
    function collapse(text) {
        return text.replace(/ {2,}/g, ' ').replace(/^ +/, '');
    }

    function digits(text) {
        return text.replace(/\D/g, '');
    }

    function decimalsOf(text) {
        var dot = text.indexOf('.');
        return dot === -1 ? 0 : text.length - dot - 1;
    }

    function pick(a, b) {
        return a !== undefined ? a : b;
    }

    var RULES = {
        // Letters (any language, including ñ and accents) and single spaces only.
        name: {
            label: 'Name', min: 2, max: 50, truncate: true,
            invalid: function (t) { return /[^\p{L} ]/u.test(t); },
            clean: function (t) { return collapse(t.replace(/[^\p{L} ]/gu, '')); },
            format: /^\p{L}+( \p{L}+)*$/u,
            formatMessage: 'can only contain letters and single spaces.',
            charMessage: 'Only letters and spaces are allowed.'
        },

        // Philippine mobile number: 11 digits, 09XXXXXXXXX.
        phone: {
            label: 'Phone number', min: 11, max: 11,
            invalid: function (t) { return /\D/.test(t); },
            clean: digits,
            format: /^09\d{9}$/,
            formatMessage: 'must be 11 digits and start with 09 (e.g. 09171234567).',
            tooLong: 'can only have 11 digits.',
            charMessage: 'Only digits are allowed.'
        },

        email: {
            label: 'Email', max: 100,
            invalid: function (t) { return /[^A-Za-z0-9._%+\-@]/.test(t); },
            clean: function (t) { return t.replace(/[^A-Za-z0-9._%+\-@]/g, ''); },
            format: /^[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}$/,
            formatMessage: 'must be a valid email address (e.g. name@example.com).',
            charMessage: 'Emails cannot contain spaces or special characters.'
        },

        // Whole numbers only. Range comes from input-rule-min / input-rule-max.
        integer: {
            label: 'Value', numeric: true,
            invalid: function (t) { return /\D/.test(t); },
            clean: digits,
            format: /^\d+$/,
            formatMessage: 'must be a whole number.',
            charMessage: 'Only whole numbers are allowed.'
        },

        // Money or area: digits with one decimal point. Places come from input-rule-decimals.
        decimal: {
            label: 'Amount', numeric: true, decimals: 2,
            invalid: function (t) { return /[^0-9.]/.test(t) || t.split('.').length > 2; },
            clean: function (t) {
                t = t.replace(/[^0-9.]/g, '');
                var dot = t.indexOf('.');
                return dot === -1 ? t : t.slice(0, dot + 1) + t.slice(dot + 1).replace(/\./g, '');
            },
            format: /^\d+(\.\d+)?$/,
            formatMessage: 'must be a number (e.g. 1500 or 1500.50).',
            charMessage: 'Only numbers and one decimal point are allowed.'
        },

        // Street, barangay, city, province. Letters, digits and common address punctuation.
        address: {
            label: 'Address', max: 150, truncate: true,
            invalid: function (t) { return /[^\p{L}\p{N} .,#\/\-()'&]/u.test(t); },
            clean: function (t) { return collapse(t.replace(/[^\p{L}\p{N} .,#\/\-()'&]/gu, '')); },
            charMessage: "Address can only contain letters, numbers and . , # / - ( ) ' &"
        },

        // Job or occupation titles.
        occupation: {
            label: 'Occupation', max: 25, truncate: true,
            invalid: function (t) { return /[^\p{L} \-\/&().']/u.test(t); },
            clean: function (t) { return collapse(t.replace(/[^\p{L} \-\/&().']/gu, '')); },
            charMessage: "Only letters, spaces and - / & ( ) . ' are allowed."
        },

        // Unit names, floors, amenities and maintenance categories: "Studio A101", "3F", "Doors & Locks", "Internet / WiFi".
        code: {
            label: 'Value', max: 50, truncate: true,
            invalid: function (t) { return /[^\p{L}\p{N} .\-#\/&]/u.test(t); },
            clean: function (t) { return collapse(t.replace(/[^\p{L}\p{N} .\-#\/&]/gu, '')); },
            charMessage: 'Only letters, numbers, spaces and . - # / & are allowed.'
        },

        // Free text (remarks, notes, reasons). Control characters are removed; line breaks are kept.
        description: {
            label: 'Description', max: 1000, truncate: true,
            invalid: function () { return false; },
            clean: function (t) { return t.replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]/g, ''); }
        },

        // One OTP / verification digit per box.
        digit: {
            label: 'Digit', max: 1,
            invalid: function (t) { return /\D/.test(t); },
            clean: digits,
            format: /^\d$/,
            formatMessage: 'must be a digit.',
            charMessage: 'Only digits are allowed.'
        },

        // Passwords may contain any character. Only length is checked.
        password: {
            label: 'Password', min: 8, max: 64, trim: false,
            invalid: function () { return false; },
            clean: function (t) { return t; }
        }
    };

    function ruleFor(rule) {
        return RULES[rule];
    }

    function normalize(rule, value) {
        var def = ruleFor(rule);
        var text = str(value);
        if (!def || def.trim === false) return text;
        text = text.trim();
        if (rule === 'decimal') text = text.replace(/\.$/, '');
        return text;
    }

    function clean(rule, text, opts) {
        var def = ruleFor(rule);
        text = str(text);
        return def ? def.clean(text, opts || {}) : text;
    }

    function isInvalidChar(rule, text) {
        var def = ruleFor(rule);
        return !!def && def.invalid(str(text));
    }

    // What to keep when input goes over its limit. Free text keeps the part that fits (so a long
    // paste is not lost). Numbers, emails and passwords keep the last valid value, because a cut
    // phone number or email would be wrong data.
    function clip(rule, text, opts, lastValid) {
        var def = ruleFor(rule);
        if (!def || !def.truncate) return lastValid;
        var max = pick((opts || {}).max, def.max);
        return max === undefined ? lastValid : text.slice(0, max);
    }

    // Message for input that must not be typed (too long, out of range, too many decimals).
    // Returns null when the cleaned text is still acceptable.
    function limitMessage(rule, text, opts) {
        var def = ruleFor(rule);
        opts = opts || {};
        if (!def) return null;
        var label = opts.label || def.label;
        var max = pick(opts.max, def.max);

        if (def.numeric) {
            if (text === '') return null;
            if (max !== undefined && Number(text) > max) return label + ' cannot be more than ' + max + '.';
            var places = pick(opts.decimals, def.decimals);
            if (places !== undefined && decimalsOf(text) > places) {
                return label + ' can have at most ' + places + ' decimal place' + (places === 1 ? '' : 's') + '.';
            }
            return null;
        }

        if (max !== undefined && text.length > max) {
            return def.tooLong ? label + ' ' + def.tooLong : label + " can't exceed " + max + ' characters.';
        }
        return null;
    }

    // Full validation for a final value. Returns a message or null.
    function validate(rule, value, opts) {
        var def = ruleFor(rule);
        opts = opts || {};
        if (!def) return null;
        var label = opts.label || def.label;
        var text = normalize(rule, value);

        if (text === '') return opts.required ? label + ' is required.' : null;
        if (def.format && !def.format.test(text)) return label + ' ' + def.formatMessage;

        var min = pick(opts.min, def.min);
        var max = pick(opts.max, def.max);

        if (def.numeric) {
            var n = Number(text);
            if (isNaN(n)) return label + ' must be a number.';
            if (min !== undefined && n < min) return label + ' must be at least ' + min + '.';
            if (max !== undefined && n > max) return label + ' cannot be more than ' + max + '.';
            var places = pick(opts.decimals, def.decimals);
            if (places !== undefined && decimalsOf(text) > places) {
                return label + ' can have at most ' + places + ' decimal place' + (places === 1 ? '' : 's') + '.';
            }
            return null;
        }

        if (min !== undefined && text.length < min) return label + ' must be at least ' + min + ' characters.';
        if (max !== undefined && text.length > max) return label + " can't exceed " + max + ' characters.';
        return null;
    }

    // Reads the rule options from the element's attributes.
    function optionsFrom(attrs) {
        var opts = { label: attrs.inputRuleLabel, required: attrs.inputRuleRequired !== undefined };
        ['min', 'max', 'decimals'].forEach(function (key) {
            var raw = attrs['inputRule' + key.charAt(0).toUpperCase() + key.slice(1)];
            if (raw !== undefined && raw !== '') opts[key] = Number(raw);
        });
        return opts;
    }

    // Runs the bound inputs under the scope and returns their messages. Hidden inputs
    // (display:none) are skipped. With a group name, only inputs tagged with that
    // input-rule-group are checked, so one form does not block another form on the same page.
    function collect(scope, group) {
        var result = { group: group, errors: [] };
        scope.$broadcast('inputRule:check', result);
        return result.errors;
    }

    return {
        RULES: RULES,
        str: str,
        normalize: normalize,
        clean: clean,
        clip: clip,
        isInvalidChar: isInvalidChar,
        limitMessage: limitMessage,
        validate: validate,
        optionsFrom: optionsFrom,
        collect: collect,
        hasRule: function (rule) { return !!ruleFor(rule); },
        charMessage: function (rule, opts) {
            var def = ruleFor(rule);
            return def && def.charMessage ? def.charMessage : null;
        }
    };
});

app.directive('inputRule', ['InputRules', function (InputRules) {
    return {
        restrict: 'A',
        require: 'ngModel',
        link: function (scope, element, attrs, ngModel) {
            var el = element[0];
            var rule = attrs.inputRule;
            if (!InputRules.hasRule(rule)) return;

            var opts = InputRules.optionsFrom(attrs);
            var lastAccepted = '';
            var composing = false;
            var errorEl = null;

            // Forms that already show their own messages (formErrors) add input-rule-no-message.
            if (rule !== 'digit' && attrs.inputRuleNoMessage === undefined) {
                // Inside an icon wrapper (.relative), the message goes after the wrapper so the icon stays centred.
                var anchor = element.parent().hasClass('relative') ? element.parent() : element;
                errorEl = angular.element('<p class="hidden mt-1 text-xs text-red-500" role="alert"></p>');
                anchor.after(errorEl);
            }

            function showError(message) {
                if (errorEl) {
                    errorEl.text(message || '');
                    errorEl.toggleClass('hidden', !message);
                }
                element.css('border-color', message ? '#f87171' : '');
                el.setAttribute('aria-invalid', message ? 'true' : 'false');
            }

            // Values set from code (edit forms, resets) become the last accepted value.
            ngModel.$formatters.push(function (value) {
                lastAccepted = InputRules.str(value);
                return value;
            });

            // Keep the model clean even when a value arrives through a path that skips the input handler.
            ngModel.$parsers.push(function (view) {
                return InputRules.clean(rule, view, opts);
            });

            function setCaret(position) {
                try { el.setSelectionRange(position, position); } catch (e) { /* input type without caret support */ }
            }

            // Runs fn inside a digest, unless one is already running (blur can fire during a digest).
            function safeApply(fn) {
                if (scope.$root.$$phase) fn();
                else scope.$apply(fn);
            }

            function applyRules() {
                var raw = el.value;
                var value = InputRules.clean(rule, raw, opts);
                var limit = InputRules.limitMessage(rule, value, opts);
                var message = null;

                if (limit) {
                    value = InputRules.clip(rule, value, opts, lastAccepted);
                    message = limit;
                } else if (InputRules.isInvalidChar(rule, raw)) {
                    message = InputRules.charMessage(rule, opts);
                }

                if (value !== raw) {
                    var caret = el.selectionStart;
                    var focused = document.activeElement === el;
                    var caretAt = caret === null ? value.length
                        : Math.min(InputRules.clean(rule, raw.slice(0, caret), opts).length, value.length);
                    el.value = value;
                    safeApply(function () { ngModel.$setViewValue(value); });
                    if (focused) setCaret(caretAt);
                }
                lastAccepted = value;
                showError(message);
            }

            element.on('compositionstart', function () { composing = true; });
            element.on('compositionend', function () { composing = false; applyRules(); });
            element.on('input', function () {
                if (!composing) applyRules();
            });

            element.on('blur', function () {
                safeApply(function () {
                    var value = InputRules.normalize(rule, ngModel.$modelValue);
                    lastAccepted = value;
                    ngModel.$setViewValue(value);
                    ngModel.$render();
                    showError(InputRules.validate(rule, value, opts));
                });
            });

            // Called by InputRules.collect() before a save.
            scope.$on('inputRule:check', function (event, result) {
                if (result.group !== undefined && result.group !== attrs.inputRuleGroup) return;
                if (el.getClientRects().length === 0) return;   // hidden field, not part of this form
                var value = InputRules.normalize(rule, ngModel.$modelValue);
                var message = InputRules.validate(rule, value, opts);
                if (value !== InputRules.str(ngModel.$modelValue)) {
                    lastAccepted = value;
                    ngModel.$setViewValue(value);
                    ngModel.$render();
                }
                showError(message);
                if (message) result.errors.push(message);
            });
        }
    };
}]);
