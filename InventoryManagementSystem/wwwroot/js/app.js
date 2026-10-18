/* ============================================================
   SIMS App JS – Sidebar, Dropdowns, Toasts, Utilities (Dark Only)
   ============================================================ */
(function () {
  'use strict';

  const byId = id => document.getElementById(id);
  const qsa  = sel => document.querySelectorAll(sel);

  /* ── Sidebar ──────────────────────────────────────── */
  function initSidebar() {
    const sidebar = byId('sidebar');
    const main    = byId('main');
    const overlay = byId('sb-overlay');
    const colBtn  = byId('sb-collapse-btn');
    const mobBtn  = byId('sb-mobile-btn');
    if (!sidebar) return;

    // Restore collapsed state on desktop
    if (window.innerWidth >= 1024 && localStorage.getItem('sims-sb-collapsed') === 'true') {
      sidebar.classList.add('collapsed');
      if (main) main.classList.add('sidebar-collapsed');
    }

    // Desktop collapse
    if (colBtn) {
      colBtn.addEventListener('click', () => {
        const c = sidebar.classList.toggle('collapsed');
        if (main) main.classList.toggle('sidebar-collapsed', c);
        localStorage.setItem('sims-sb-collapsed', c);
      });
    }

    // Mobile open
    if (mobBtn) {
      mobBtn.addEventListener('click', () => openMobile());
    }

    // Overlay close
    if (overlay) {
      overlay.addEventListener('click', closeMobile);
    }

    document.addEventListener('keydown', e => { if (e.key === 'Escape') closeMobile(); });
  }

  function openMobile() {
    const sidebar = byId('sidebar');
    const overlay = byId('sb-overlay');
    if (sidebar) sidebar.classList.add('sb-mobile-open');
    if (overlay) overlay.classList.add('show');
    document.body.style.overflow = 'hidden';
  }

  function closeMobile() {
    const sidebar = byId('sidebar');
    const overlay = byId('sb-overlay');
    if (sidebar) sidebar.classList.remove('sb-mobile-open');
    if (overlay) overlay.classList.remove('show');
    document.body.style.overflow = '';
  }

  /* ── Dropdowns (Custom Single-Click Event Delegation) ─ */
  function initDropdowns() {
    document.addEventListener('click', function (e) {
      const toggleBtn = e.target.closest('[data-dd-toggle="dropdown"], [data-bs-toggle="dropdown"]');

      if (toggleBtn) {
        // If it's a modal trigger inside a dropdown item, let modal handling take over
        if (e.target.closest('[data-bs-toggle="modal"]')) {
          // allow modal toggle
        } else {
          e.preventDefault();
        }
        e.stopPropagation();

        const parent = toggleBtn.closest('.dropdown') || toggleBtn.parentElement;
        if (!parent) return;

        const menu = parent.querySelector('.dropdown-menu');
        if (!menu) return;

        const isCurrentlyOpen = menu.classList.contains('show');

        // Close all other open dropdown menus
        document.querySelectorAll('.dropdown-menu.show').forEach(m => {
          if (m !== menu) m.classList.remove('show');
        });
        document.querySelectorAll('[data-dd-toggle="dropdown"], [data-bs-toggle="dropdown"]').forEach(b => {
          if (b !== toggleBtn) b.setAttribute('aria-expanded', 'false');
        });

        // Toggle target dropdown menu cleanly
        if (isCurrentlyOpen) {
          menu.classList.remove('show');
          toggleBtn.setAttribute('aria-expanded', 'false');
        } else {
          menu.classList.add('show');
          toggleBtn.setAttribute('aria-expanded', 'true');
        }
      } else if (!e.target.closest('.dropdown-menu')) {
        // Dismiss dropdowns when clicking anywhere outside
        document.querySelectorAll('.dropdown-menu.show').forEach(m => m.classList.remove('show'));
        document.querySelectorAll('[data-dd-toggle="dropdown"], [data-bs-toggle="dropdown"]').forEach(b => b.setAttribute('aria-expanded', 'false'));
      }
    });
  }

  /* ── Tooltips ──────────────────────────────────────── */
  function initTooltips() {
    if (typeof bootstrap === 'undefined') return;
    qsa('[data-bs-toggle="tooltip"]').forEach(el => {
      try {
        new bootstrap.Tooltip(el, { placement: 'right', trigger: 'hover' });
      } catch (err) {}
    });
  }

  /* ── Date ──────────────────────────────────────────── */
  function renderDate() {
    const el = byId('topbar-date');
    if (!el) return;
    el.textContent = new Date().toLocaleDateString('en-IN', { timeZone: 'Asia/Kolkata', weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' }) + ' (IST)';
  }

  /* ── Notifications ─────────────────────────────────── */
  function timeAgo(dateString) {
    if (!dateString) return '';
    const date = new Date(dateString);
    if (isNaN(date.getTime())) return '';
    const seconds = Math.floor((new Date() - date) / 1000);
    if (seconds < 60) return 'Just now';
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    if (days < 30) return `${days}d ago`;
    return date.toLocaleDateString('en-IN', { month: 'short', day: 'numeric' });
  }

  function getNotifIconInfo(type, title) {
    const t = (type || '').toLowerCase();
    const ttl = (title || '').toLowerCase();
    if (t === 'danger' || ttl.includes('delete') || ttl.includes('out of stock') || ttl.includes('critical')) {
      return { icon: 'bi-trash3-fill', cls: 'notif-icon-danger' };
    }
    if (t === 'warning' || ttl.includes('low stock') || ttl.includes('warning')) {
      return { icon: 'bi-exclamation-triangle-fill', cls: 'notif-icon-warning' };
    }
    if (t === 'success' || ttl.includes('sale') || ttl.includes('payment') || ttl.includes('completed')) {
      return { icon: 'bi-check-circle-fill', cls: 'notif-icon-success' };
    }
    return { icon: 'bi-info-circle-fill', cls: 'notif-icon-info' };
  }

  function escapeHtml(str) {
    if (!str) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  function markAllNotificationsRead(e) {
    if (e) {
      e.preventDefault();
      e.stopPropagation();
    }
    const markBtn = byId('notif-mark-read-btn');
    if (markBtn) {
      markBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" style="width:12px;height:12px;margin-right:4px"></span> Marking...';
      markBtn.disabled = true;
    }

    fetch('/api/notifications/mark-read', { method: 'POST' })
      .then(r => r.ok ? r.json() : null)
      .then(() => {
        loadNotifications();
        if (typeof showToast === 'function') {
          showToast('All notifications marked as read', 'success');
        }
      })
      .catch(() => {
        if (markBtn) {
          markBtn.innerHTML = '<i class="bi bi-check2-all"></i> Mark all read';
          markBtn.disabled = false;
        }
      });
  }

  function loadNotifications() {
    const list       = byId('notif-list');
    const badge      = byId('notif-badge');
    const unreadPill = byId('notif-unread-count');
    const markBtn    = byId('notif-mark-read-btn');
    if (!list) return;

    fetch('/api/notifications')
      .then(r => r.ok ? r.json() : [])
      .then(data => {
        if (!data || !data.length) {
          list.innerHTML = `
            <div class="empty-s" style="padding:28px 16px;text-align:center">
              <i class="bi bi-bell-slash empty-s-icon" style="font-size:24px;margin-bottom:8px;display:block"></i>
              <div class="empty-s-title" style="font-size:13px;font-weight:600">No notifications</div>
              <div class="empty-s-sub" style="font-size:12px;color:var(--text-muted);margin-top:2px">You're all caught up!</div>
            </div>`;
          if (badge) badge.style.display = 'none';
          if (unreadPill) unreadPill.style.display = 'none';
          if (markBtn) {
            markBtn.innerHTML = 'Recent alerts';
            markBtn.title = 'Recent alerts';
            markBtn.disabled = true;
            markBtn.onclick = null;
          }
          return;
        }

        if (badge) badge.style.display = 'block';
        if (unreadPill) {
          unreadPill.style.display = 'inline-block';
          unreadPill.textContent = `${data.length} new`;
        }
        if (markBtn) {
          markBtn.innerHTML = '<i class="bi bi-check2-all"></i> Mark all read';
          markBtn.title = 'Mark all as read';
          markBtn.disabled = false;
          markBtn.onclick = markAllNotificationsRead;
        }

        list.innerHTML = data.slice(0, 15).map(n => {
          const iconInfo = getNotifIconInfo(n.type, n.title);
          const timeStr  = timeAgo(n.timestamp);
          return `
            <div class="notif-card" role="button" tabindex="0">
              <div class="notif-icon-w ${iconInfo.cls}">
                <i class="bi ${iconInfo.icon}"></i>
              </div>
              <div class="notif-body">
                <div class="notif-topline">
                  <h6 class="notif-title">${escapeHtml(n.title)}</h6>
                  ${timeStr ? `<span class="notif-time">${timeStr}</span>` : ''}
                </div>
                <p class="notif-desc">${escapeHtml(n.message)}</p>
              </div>
            </div>`;
        }).join('');
      })
      .catch(() => {
        if (list) {
          list.innerHTML = `
            <div class="empty-s" style="padding:28px 16px;text-align:center">
              <i class="bi bi-bell-slash empty-s-icon" style="font-size:24px;margin-bottom:8px;display:block"></i>
              <div class="empty-s-title" style="font-size:13px;font-weight:600">No notifications</div>
            </div>`;
        }
      });
  }

  /* ── Toast ─────────────────────────────────────────── */
  const ICONS  = { success:'bi-check-circle-fill', danger:'bi-x-circle-fill', warning:'bi-exclamation-triangle-fill', info:'bi-info-circle-fill' };
  const TITLES = { success:'Success', danger:'Error', warning:'Warning', info:'Info' };

  function showToast(message, type = 'info') {
    const stack = byId('toast-stack');
    if (!stack) return;
    const el = document.createElement('div');
    el.className = `toast-item toast-${type}`;
    el.innerHTML = `
      <div class="toast-icon-w"><i class="bi ${ICONS[type]||ICONS.info}"></i></div>
      <div class="toast-body">
        <div class="toast-title">${TITLES[type]||'Notice'}</div>
        <div class="toast-msg">${message}</div>
      </div>
      <button class="toast-close" onclick="this.closest('.toast-item').remove()"><i class="bi bi-x"></i></button>`;
    stack.appendChild(el);
    setTimeout(() => { el.classList.add('hiding'); setTimeout(() => el.remove(), 250); }, 4200);
  }

  /* ── Table search ──────────────────────────────────── */
  function initTableSearch() {
    qsa('[data-tbl-search]').forEach(input => {
      const tbl = byId(input.getAttribute('data-tbl-search'));
      if (!tbl) return;
      let timer = null;
      input.addEventListener('input', function () {
        clearTimeout(timer);
        const self = this;
        timer = setTimeout(() => {
          const q = self.value.toLowerCase();
          tbl.querySelectorAll('tbody tr').forEach(row => {
            row.style.display = !q || row.textContent.toLowerCase().includes(q) ? '' : 'none';
          });
        }, 150);
      });
    });
  }

  /* ── Confirm delete ────────────────────────────────── */
  function initConfirm() {
    qsa('[data-confirm]').forEach(btn => {
      btn.addEventListener('click', function (e) {
        if (!confirm(this.getAttribute('data-confirm') || 'Are you sure?')) e.preventDefault();
      });
    });
  }

  /* ── Image preview ─────────────────────────────────── */
  function initImgPreview() {
    qsa('[data-preview]').forEach(input => {
      input.addEventListener('change', function () {
        const img = byId(this.getAttribute('data-preview'));
        if (!img || !this.files[0]) return;
        const reader = new FileReader();
        reader.onload = e => { img.src = e.target.result; };
        reader.readAsDataURL(this.files[0]);
      });
    });
  }

  /* ── Real-Time Permission & Session Polling ────────── */
  function initRealtimePermissionSync() {
    const currentVersion = parseInt(document.body.dataset.permVersion || '0', 10);

    function checkLiveStatus() {
      fetch('/api/account/live-status')
        .then(r => r.ok ? r.json() : null)
        .then(data => {
          if (!data) return;

          // Forced Logout if account locked or deleted
          if (data.authenticated && (data.isLocked || data.isDeleted)) {
            window.location.href = '/Account/Login';
            return;
          }

          // Dynamic UI / Sidebar update if permission version changed
          if (data.authenticated && data.version > 0 && currentVersion > 0 && data.version !== currentVersion) {
            window.location.reload();
          }
        })
        .catch(() => {});
    }

    // Poll every 8 seconds for live sync
    setInterval(checkLiveStatus, 8000);
  }

  /* ── Init ──────────────────────────────────────────── */
  document.addEventListener('DOMContentLoaded', () => {
    initSidebar();
    initDropdowns();
    initTooltips();
    renderDate();
    loadNotifications();
    initTableSearch();
    initConfirm();
    initImgPreview();
    initRealtimePermissionSync();

    // Flash message from server
    const msg  = document.body.dataset.flashMsg;
    const type = document.body.dataset.flashType;
    if (msg) showToast(msg, type || 'info');
  });

  window.showToast = showToast;
})();
