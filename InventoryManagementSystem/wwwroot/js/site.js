// Global Client-Side Input Sanitizer, Country Code Enhancer & Live Form Validator for SIMS

document.addEventListener('DOMContentLoaded', function () {
    initGlobalInputValidators();
});

// Automatically relocate any modal element directly to document.body when opening.
// This prevents CSS stacking context issues, parent backdrop blur traps, and unclickable overlay bugs
document.addEventListener('show.bs.modal', function (e) {
    var modalEl = e.target;
    if (modalEl && modalEl.classList && modalEl.classList.contains('modal')) {
        if (modalEl.parentNode !== document.body) {
            document.body.appendChild(modalEl);
        }
    }
}, true);

// Re-run initializer dynamically when Bootstrap modals open or DOM updates
document.addEventListener('shown.bs.modal', function () {
    initGlobalInputValidators();
});

// 1. STRICT KEYDOWN INTERCEPTION (CAPTURE PHASE)
// Intercepts and physically blocks alphabet/symbol keypresses BEFORE they enter phone or IMEI inputs
window.addEventListener('keydown', function (e) {
    const target = e.target;
    if (!target || target.tagName !== 'INPUT') return;

    const isPhone = isPhoneInput(target);
    const isImei = isImeiInput(target);

    if (!isPhone && !isImei) return;

    // Allow control keys: Backspace, Delete, Tab, Escape, Enter, Arrows, Home, End, Ctrl/Cmd shortcuts
    if (e.isComposing || e.key === 'Backspace' || e.key === 'Delete' || e.key === 'Tab' ||
        e.key === 'Escape' || e.key === 'Enter' || e.key.startsWith('Arrow') ||
        e.key === 'Home' || e.key === 'End' || e.ctrlKey || e.metaKey) {
        return;
    }

    if (isPhone) {
        // Allow digits 0-9 and optional '+'
        if (!/[\d+]/.test(e.key)) {
            e.preventDefault();
            e.stopPropagation();
            setLiveFeedback(target, false, '⚠️ Alphabets and special characters are not allowed in contact number. Digits only!');
            return false;
        }
    }

    if (isImei) {
        // Allow digits 0-9 only
        if (!/\d/.test(e.key)) {
            e.preventDefault();
            e.stopPropagation();
            setLiveFeedback(target, false, '⚠️ Alphabets and special characters are not allowed in IMEI number. Digits only!');
            return false;
        }
    }
}, true);

// 2. INSTANT INPUT & PASTE SANITIZATION + LIVE VALIDATION
window.addEventListener('input', function (e) {
    const target = e.target;
    if (!target || target.tagName !== 'INPUT') return;

    if (isPhoneInput(target)) {
        sanitizeAndValidatePhone(target);
    } else if (isImeiInput(target)) {
        sanitizeAndValidateImei(target);
    } else if (isEmailInput(target)) {
        validateEmail(target);
    }
}, true);

// Trigger live IMEI uniqueness check on blur
window.addEventListener('blur', function (e) {
    const target = e.target;
    if (target && target.tagName === 'INPUT' && isImeiInput(target)) {
        sanitizeAndValidateImei(target);
    }
}, true);

// 3. GLOBAL PRE-SUBMIT VALIDATION & MODAL AJAX HANDLING
document.addEventListener('submit', async function (e) {
    const form = e.target;
    if (!form || form.tagName !== 'FORM') return;

    // A. Validate Phone Inputs
    let hasError = false;
    let firstErrorInput = null;

    const phoneInputs = Array.from(form.querySelectorAll('input[type="tel"], .phone-input, input[name*="Phone" i], input[name*="Mobile" i], input[id*="Phone" i], input[id*="Mobile" i]'))
        .filter(input => isPhoneInput(input));
    phoneInputs.forEach(input => {
        const isValid = sanitizeAndValidatePhone(input, true);
        if (!isValid) {
            hasError = true;
            if (!firstErrorInput) firstErrorInput = input;
        }
    });

    // Validate IMEI Inputs
    const imeiInputs = form.querySelectorAll('.imei-input, .imei1-val, .imei2-val, input[name*="IMEI" i], input[name*="Imei" i], input[id*="IMEI" i], input[id*="Imei" i]');
    for (let input of imeiInputs) {
        const isValid = await sanitizeAndValidateImei(input, true);
        if (!isValid) {
            hasError = true;
            if (!firstErrorInput) firstErrorInput = input;
        }
    }

    // Validate Email Inputs
    const emailInputs = form.querySelectorAll('input[type="email"], .email-input, input[name*="Email" i], input[id*="Email" i]');
    emailInputs.forEach(input => {
        const isValid = validateEmail(input, true);
        if (!isValid) {
            hasError = true;
            if (!firstErrorInput) firstErrorInput = input;
        }
    });

    if (hasError) {
        e.preventDefault();
        e.stopPropagation();
        if (firstErrorInput) {
            firstErrorInput.focus();
            firstErrorInput.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
        if (window.showToast) {
            window.showToast('Please correct the highlighted validation errors before submitting.', 'danger');
        }
        return false;
    }

    // B. Modal Form AJAX submission handler to PRESERVE FORM DATA on server error
    const modal = form.closest('.modal');
    if (modal) {
        e.preventDefault();
        e.stopPropagation();

        const submitBtn = form.querySelector('button[type="submit"]');
        const originalBtnText = submitBtn ? submitBtn.innerHTML : '';

        if (submitBtn) {
            submitBtn.disabled = true;
            submitBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Processing...';
        }

        // Clear existing modal alert banner
        const existingAlert = modal.querySelector('.modal-error-alert');
        if (existingAlert) existingAlert.remove();

        try {
            const formData = new FormData(form);
            const response = await fetch(form.action || window.location.href, {
                method: form.method || 'POST',
                body: formData,
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });

            const contentType = response.headers.get('content-type') || '';
            let resData = null;
            if (contentType.includes('application/json')) {
                resData = await response.json();
            }

            if (resData) {
                if (resData.success) {
                    // Success: Close modal & refresh
                    const bsModal = bootstrap.Modal.getInstance(modal);
                    if (bsModal) bsModal.hide();
                    if (window.showToast) {
                        window.showToast(resData.message || 'Saved successfully!', 'success');
                    }
                    setTimeout(() => {
                        window.location.reload();
                    }, 500);
                } else {
                    // Error: KEEP MODAL OPEN! PRESERVE ALL FILLED FORM DATA!
                    showModalError(modal, form, resData.message || 'Submission failed. Please check form entries.');
                }
            } else {
                if (response.ok) {
                    window.location.reload();
                } else {
                    showModalError(modal, form, 'Server error processing request. Please verify inputs.');
                }
            }
        } catch (err) {
            showModalError(modal, form, 'Network / Server error: ' + err.message);
        } finally {
            if (submitBtn) {
                submitBtn.disabled = false;
                submitBtn.innerHTML = originalBtnText;
            }
        }
        return false;
    }
}, true);

function showModalError(modal, form, message) {
    let alertBox = modal.querySelector('.modal-error-alert');
    if (!alertBox) {
        alertBox = document.createElement('div');
        alertBox.className = 'alert alert-danger alert-dismissible fade show modal-error-alert mb-3 text-light';
        alertBox.style.cssText = 'background: #7F1D1D; border: 1px solid #EF4444; color: #FCA5A5; font-weight: 500; font-size: 13px; margin: 15px; border-radius: 8px;';
        alertBox.role = 'alert';
        
        const modalBody = modal.querySelector('.modal-body') || modal;
        modalBody.insertBefore(alertBox, modalBody.firstChild);
    }

    alertBox.innerHTML = `
        <div class="d-flex align-items-center">
            <i class="bi bi-exclamation-triangle-fill fs-5 me-2 text-danger"></i>
            <div><strong>Submission Failed:</strong> ${message}</div>
        </div>
        <button type="button" class="btn-close btn-close-white ms-auto" onclick="this.parentElement.style.display='none'"></button>
    `;
    alertBox.style.display = 'block';

    if (window.showToast) {
        window.showToast(message, 'danger');
    }

    // Scroll modal body to top so admin immediately sees error
    const modalBody = modal.querySelector('.modal-body');
    if (modalBody) modalBody.scrollTop = 0;
}

// HELPER FUNCTIONS
function isPhoneInput(target) {
    if (!target || target.type === 'file' || target.type === 'checkbox' || target.type === 'radio' || target.type === 'hidden' || target.type === 'search') return false;

    const name = (target.name || '').toLowerCase();
    const id = (target.id || '').toLowerCase();
    const placeholder = (target.placeholder || '').toLowerCase();

    // Ignore search and filter inputs
    if (name.includes('search') || id.includes('search') || placeholder.includes('search') || name.includes('keyword') || id.includes('keyword') || placeholder.includes('keyword') || name.includes('filter') || id.includes('filter')) {
        return false;
    }

    if (/(person|contactperson|contact_person|name|model|brand|product|device|spec|storage|color|serial|iphone|sku|category)/i.test(name + ' ' + id + ' ' + placeholder)) {
        return false;
    }

    if (target.classList.contains('phone-input') || target.type === 'tel') {
        return true;
    }

    const phonePattern = /(customerphone|contactphone|userphone|phone_number|phonenumber|contactno|mobile_number|mobilenumber|supplierphone|^phone$|^mobile$)/i;
    return phonePattern.test(name) || phonePattern.test(id);
}

function isImeiInput(target) {
    if (!target || target.type === 'file' || target.type === 'checkbox' || target.type === 'radio' || target.type === 'hidden' || target.type === 'search') return false;

    const name = (target.name || '').toLowerCase();
    const id = (target.id || '').toLowerCase();
    const placeholder = (target.placeholder || '').toLowerCase();

    // Ignore search and filter inputs
    if (name.includes('search') || id.includes('search') || placeholder.includes('search') || name.includes('keyword') || id.includes('keyword') || placeholder.includes('keyword') || name.includes('filter') || id.includes('filter')) {
        return false;
    }

    if (target.classList.contains('imei-input') || target.classList.contains('imei1-val') || target.classList.contains('imei2-val')) {
        return true;
    }

    const imeiPattern = /(oldimei|newimei|^imei|^imei1$|^imei2$|device_imei)/i;
    return imeiPattern.test(name) || imeiPattern.test(id);
}

function isEmailInput(target) {
    if (!target) return false;
    return target.type === 'email' ||
        target.classList.contains('email-input') ||
        (target.name && /email/i.test(target.name)) ||
        (target.id && /email/i.test(target.id));
}

// Country Code & Feedback Initializer
function initGlobalInputValidators() {
    document.querySelectorAll('input').forEach(input => {
        if (isPhoneInput(input)) {
            input.classList.add('phone-input');
            attachCountryCodeSelector(input);
            attachFeedbackElement(input);
        } else if (isImeiInput(input)) {
            input.classList.add('imei-input');
            attachFeedbackElement(input);
        } else if (isEmailInput(input)) {
            input.classList.add('email-input');
            attachFeedbackElement(input);
        }
    });
}

function attachCountryCodeSelector(input) {
    if (input.dataset.ccInit === 'true') return;
    input.dataset.ccInit = 'true';

    let parent = input.parentElement;
    let group = (parent.classList.contains('input-group') || parent.classList.contains('fg-prefix-wrap') || parent.classList.contains('phone-cc-group')) ? parent : null;

    if (!group) {
        group = document.createElement('div');
        group.className = 'input-group phone-cc-group';
        parent.insertBefore(group, input);
        group.appendChild(input);
    } else {
        group.classList.add('phone-cc-group');
        const oldPrefix = group.querySelector('.fg-prefix');
        if (oldPrefix) oldPrefix.remove();
    }

    let ccSelect = group.querySelector('.country-code-select');
    if (!ccSelect) {
        ccSelect = document.createElement('select');
        ccSelect.className = 'form-select country-code-select';
        ccSelect.innerHTML = `
            <option value="+91" selected>🇮🇳 +91</option>
            <option value="+1">🇺🇸 +1</option>
            <option value="+44">🇬🇧 +44</option>
            <option value="+971">🇦🇪 +971</option>
            <option value="+65">🇸🇬 +65</option>
            <option value="+61">🇦🇺 +61</option>
            <option value="+966">🇸🇦 +966</option>
            <option value="+974">🇶🇦 +974</option>
            <option value="+965">🇰🇼 +965</option>
            <option value="+968">🇴🇲 +968</option>
            <option value="+973">🇧🇭 +973</option>
            <option value="+977">🇳🇵 +977</option>
            <option value="+880">🇧🇩 +880</option>
            <option value="+94">🇱🇰 +94</option>
            <option value="+60">🇲🇾 +60</option>
            <option value="+49">🇩🇪 +49</option>
            <option value="+33">🇫🇷 +33</option>
            <option value="+86">🇨🇳 +86</option>
            <option value="+81">🇯🇵 +81</option>
        `;
        group.insertBefore(ccSelect, input);
    }

    ccSelect.addEventListener('change', function () {
        sanitizeAndValidatePhone(input);
    });
}

function attachFeedbackElement(input) {
    if (input.nextElementSibling && input.nextElementSibling.classList.contains('field-feedback-msg')) return;

    let parent = input.closest('.input-group') || input.parentElement;
    if (parent.nextElementSibling && parent.nextElementSibling.classList.contains('field-feedback-msg')) return;

    const msgEl = document.createElement('div');
    msgEl.className = 'field-feedback-msg mt-1 font-monospace';
    msgEl.style.cssText = 'font-size: 11px; display: none; line-height: 1.3; transition: all 0.2s ease;';
    
    if (parent.classList.contains('input-group')) {
        parent.parentElement.insertBefore(msgEl, parent.nextSibling);
    } else {
        parent.appendChild(msgEl);
    }
}

function setLiveFeedback(input, isValid, message) {
    attachFeedbackElement(input);
    let parent = input.closest('.input-group') || input.parentElement;
    let msgEl = parent.parentElement.querySelector('.field-feedback-msg') || parent.querySelector('.field-feedback-msg');
    
    if (!msgEl) return;

    if (!message) {
        msgEl.style.display = 'none';
        input.style.borderColor = '';
        return;
    }

    msgEl.style.display = 'block';
    if (isValid) {
        msgEl.className = 'field-feedback-msg text-success mt-1 small font-monospace';
        msgEl.innerHTML = `<i class="bi bi-check-circle-fill me-1"></i> ${message}`;
        input.style.borderColor = 'var(--success, #10B981)';
    } else {
        msgEl.className = 'field-feedback-msg text-danger mt-1 small font-monospace';
        msgEl.innerHTML = `<i class="bi bi-exclamation-triangle-fill me-1"></i> ${message}`;
        input.style.borderColor = 'var(--danger, #EF4444)';
    }
}

function sanitizeAndValidatePhone(input, isSubmitCheck = false) {
    let group = input.closest('.input-group');
    let ccSelect = group ? group.querySelector('.country-code-select') : null;
    let cc = ccSelect ? ccSelect.value : '+91';

    if (cc === '+91') {
        input.setAttribute('maxlength', '10');
        input.placeholder = 'e.g. 9876543210';
    } else {
        input.setAttribute('maxlength', '15');
        input.placeholder = 'Enter mobile number';
    }

    let rawVal = input.value;
    let cleanVal = rawVal.replace(/[^\d+]/g, '');
    let digitsOnly = cleanVal.replace(/\D/g, '');

    const maxLen = cc === '+91' ? 10 : 15;
    if (digitsOnly.length > maxLen) {
        digitsOnly = digitsOnly.slice(0, maxLen);
    }

    input.value = digitsOnly;

    const isRequired = input.hasAttribute('required');
    if (!isRequired && digitsOnly.length === 0) {
        setLiveFeedback(input, true, '');
        return true;
    }

    if (digitsOnly.length === 0) {
        if (isRequired) {
            setLiveFeedback(input, false, 'Mobile phone number is required.');
            return false;
        } else {
            setLiveFeedback(input, true, '');
            return true;
        }
    }

    if (cc === '+91') {
        if (digitsOnly.length < 10) {
            setLiveFeedback(input, false, `Phone number must be exactly 10 digits for India (${cc}). Currently: ${digitsOnly.length} digit(s).`);
            return false;
        } else if (digitsOnly.length === 10) {
            setLiveFeedback(input, true, `Valid 10-Digit Mobile: ${cc} ${digitsOnly}`);
            return true;
        }
    } else {
        if (digitsOnly.length < 7 || digitsOnly.length > 15) {
            setLiveFeedback(input, false, `International number (${cc}) must contain between 7 and 15 digits. Currently: ${digitsOnly.length} digit(s).`);
            return false;
        } else {
            setLiveFeedback(input, true, `Valid International Mobile: ${cc} ${digitsOnly}`);
            return true;
        }
    }
}

async function sanitizeAndValidateImei(input, isSubmitCheck = false) {
    let rawVal = input.value;
    let digitsOnly = rawVal.replace(/\D/g, '').slice(0, 16);
    input.value = digitsOnly;

    const isRequired = input.hasAttribute('required');
    if (!isRequired && digitsOnly.length === 0) {
        setLiveFeedback(input, true, '');
        return true;
    }

    if (digitsOnly.length === 0) {
        if (isRequired) {
            setLiveFeedback(input, false, 'IMEI number is required.');
            return false;
        } else {
            setLiveFeedback(input, true, '');
            return true;
        }
    } else if (digitsOnly.length < 14 || digitsOnly.length > 16) {
        setLiveFeedback(input, false, `IMEI number must be 14 to 16 numeric digits. Currently: ${digitsOnly.length} digit(s).`);
        return false;
    } else {
        // Live AJAX Duplicate IMEI Check
        if (input.dataset.lastCheckedImei !== digitsOnly) {
            input.dataset.lastCheckedImei = digitsOnly;
            try {
                const resp = await fetch(`/Imei/ValidateImei?imei=${digitsOnly}`);
                const data = await resp.json();
                if (!data.available) {
                    input.dataset.isDuplicate = 'true';
                    setLiveFeedback(input, false, `This IMEI '${digitsOnly}' already exists in inventory! Duplicate IMEI cannot be accepted.`);
                    return false;
                } else {
                    delete input.dataset.isDuplicate;
                    setLiveFeedback(input, true, `Valid & Available IMEI: ${digitsOnly}`);
                    return true;
                }
            } catch (err) {
                setLiveFeedback(input, true, `Valid IMEI Format: ${digitsOnly}`);
                return true;
            }
        } else if (input.dataset.isDuplicate === 'true') {
            setLiveFeedback(input, false, `This IMEI '${digitsOnly}' already exists in inventory! Duplicate IMEI cannot be accepted.`);
            return false;
        } else {
            setLiveFeedback(input, true, `Valid & Available IMEI: ${digitsOnly}`);
            return true;
        }
    }
}

function validateEmail(input, isSubmitCheck = false) {
    let val = input.value.trim();
    const isRequired = input.hasAttribute('required');

    if (!isRequired && val.length === 0) {
        setLiveFeedback(input, true, '');
        return true;
    }

    if (val.length === 0) {
        if (isRequired) {
            setLiveFeedback(input, false, 'Email address is required.');
            return false;
        } else {
            setLiveFeedback(input, true, '');
            return true;
        }
    }

    const emailRegex = /^[\w\.-]+@[\w\.-]+\.\w{2,}$/;
    if (!emailRegex.test(val)) {
        setLiveFeedback(input, false, 'Invalid email format (e.g. customer@domain.com).');
        return false;
    } else {
        setLiveFeedback(input, true, 'Valid email address format.');
        return true;
    }
}

// 4. GLOBAL FILTER TOGGLE ENGINE (PERSISTED IN LOCALSTORAGE)
function toggleFilterPanel(panelId, btnId) {
    if (!panelId) panelId = 'filterPanel';
    const panel = document.getElementById(panelId);
    if (!panel) return;

    const btn = btnId ? document.getElementById(btnId) : null;
    const isCurrentlyHidden = (panel.style.display === 'none' || getComputedStyle(panel).display === 'none');

    if (isCurrentlyHidden) {
        panel.style.display = 'block';
        localStorage.setItem('sims_filter_state_' + panelId, 'on');
        if (btn) {
            btn.classList.remove('btn-secondary');
            btn.classList.add('btn-outline-primary', 'btn-outline');
            btn.innerHTML = `<i class="bi bi-funnel-fill text-primary me-1"></i><span>Filters: ON</span>`;
        }
    } else {
        panel.style.display = 'none';
        localStorage.setItem('sims_filter_state_' + panelId, 'off');
        if (btn) {
            btn.classList.remove('btn-outline-primary', 'btn-outline');
            btn.classList.add('btn-secondary');
            btn.innerHTML = `<i class="bi bi-funnel me-1"></i><span>Filters: OFF</span>`;
        }
    }
}

// Auto-restore stored filter state on DOM load & init Table Drag Scroll
document.addEventListener('DOMContentLoaded', function () {
    const filterPanels = document.querySelectorAll('[id$="FilterPanel"]');
    filterPanels.forEach(panel => {
        const panelId = panel.id;
        const btnId = panelId.replace('Panel', 'ToggleBtn');
        const storedState = localStorage.getItem('sims_filter_state_' + panelId);
        
        if (storedState === 'off') {
            panel.style.display = 'none';
            const btn = document.getElementById(btnId);
            if (btn) {
                btn.classList.remove('btn-outline-primary', 'btn-outline');
                btn.classList.add('btn-secondary');
                btn.innerHTML = `<i class="bi bi-funnel me-1"></i><span>Filters: OFF</span>`;
            }
        }
    });

    initGlobalTableDragScroll();
    setInterval(initGlobalTableDragScroll, 1000);
});

// 5. SITE-WIDE HORIZONTAL TABLE MOUSE-DRAG SCROLLING ENGINE
function initGlobalTableDragScroll() {
    const tableContainers = document.querySelectorAll('.tbl-scroll, .table-responsive, div:has(> table)');

    tableContainers.forEach(container => {
        if (container.dataset.dragScrollBound === 'true') return;

        const table = container.querySelector('table');
        if (!table) return;

        container.dataset.dragScrollBound = 'true';

        let isMouseDown = false;
        let startX = 0;
        let startScrollLeft = 0;
        let isDragging = false;

        container.addEventListener('mousedown', function (e) {
            // Ignore non-primary mouse clicks (e.g. right click)
            if (e.button !== 0) return;

            // Do not drag scroll if user is interacting with form controls or buttons
            if (e.target.closest('button, a, input, select, textarea, label, [role="button"], .btn-d, .btn')) {
                return;
            }

            isMouseDown = true;
            isDragging = false;
            startX = e.clientX;
            startScrollLeft = container.scrollLeft;
        });

        window.addEventListener('mouseup', function () {
            if (isMouseDown) {
                isMouseDown = false;
                container.style.cursor = '';
                setTimeout(() => { isDragging = false; }, 50);
            }
        });

        container.addEventListener('mouseleave', function () {
            if (isMouseDown) {
                isMouseDown = false;
                container.style.cursor = '';
                setTimeout(() => { isDragging = false; }, 50);
            }
        });

        container.addEventListener('mousemove', function (e) {
            if (!isMouseDown) return;

            const deltaX = e.clientX - startX;

            // Only trigger horizontal drag scroll if moved > 4px
            if (Math.abs(deltaX) > 4) {
                isDragging = true;
                container.style.cursor = 'grabbing';
                container.scrollLeft = startScrollLeft - (deltaX * 1.5);
            }
        });

        // Prevent accidental button/modal/link clicks if user was dragging to scroll
        container.addEventListener('click', function (e) {
            if (isDragging) {
                e.preventDefault();
                e.stopPropagation();
            }
        }, true);

        // Ensure vertical mouse wheel scrolling over table containers scrolls the page vertically
        container.addEventListener('wheel', function (e) {
            if (Math.abs(e.deltaY) > Math.abs(e.deltaX)) {
                window.scrollBy(0, e.deltaY);
            }
        }, { passive: true });
    });
}

// ============================================================
// SIMS THEME MANAGEMENT ENGINE (DARK & LIGHT MODE)
// ============================================================
(function () {
    // Synchronous initial theme setup
    var savedTheme = localStorage.getItem('sims_theme');
    var isAuthPage = document.body && document.body.classList.contains('auth-page-body');
    var activeTheme = isAuthPage ? 'dark' : (savedTheme === 'light' ? 'light' : 'dark');

    document.documentElement.setAttribute('data-theme', activeTheme);

    window.getSIMSTheme = function () {
        return document.documentElement.getAttribute('data-theme') || 'dark';
    };

    window.setSIMSTheme = function (theme) {
        var isAuth = document.body && document.body.classList.contains('auth-page-body');
        var newTheme = isAuth ? 'dark' : (theme === 'light' ? 'light' : 'dark');
        
        document.documentElement.setAttribute('data-theme', newTheme);
        if (!isAuth) {
            localStorage.setItem('sims_theme', newTheme);
        }

        updateThemeToggleIcons(newTheme);

        if (window.updateChartsTheme) {
            window.updateChartsTheme(newTheme);
        }
    };

    window.toggleSIMSTheme = function () {
        var current = window.getSIMSTheme();
        var next = (current === 'light') ? 'dark' : 'light';
        window.setSIMSTheme(next);
    };

    function updateThemeToggleIcons(theme) {
        var toggleBtns = document.querySelectorAll('.theme-toggle-btn');
        toggleBtns.forEach(function (btn) {
            var icon = btn.querySelector('i');
            if (icon) {
                if (theme === 'light') {
                    icon.className = 'bi bi-moon-stars-fill';
                    btn.setAttribute('title', 'Switch to Dark Mode');
                    btn.setAttribute('aria-label', 'Switch to Dark Mode');
                } else {
                    icon.className = 'bi bi-sun-fill';
                    btn.setAttribute('title', 'Switch to Light Mode');
                    btn.setAttribute('aria-label', 'Switch to Light Mode');
                }
            }
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        updateThemeToggleIcons(window.getSIMSTheme());
    });
})();

