/* ==========================================================================
   DATSAN247 — tương tác giao diện dùng chung
   - Header đổ bóng khi cuộn, nút lên đầu trang
   - Hiệu ứng xuất hiện khi cuộn (data-reveal)
   - Trạng thái "đang xử lý" cho nút submit (chống bấm 2 lần)
   - Hộp xác nhận dùng chung (form[data-confirm-message])
   - Sân yêu thích (AJAX), chia sẻ link, sân đã xem gần đây
   ========================================================================== */
(function () {
    'use strict';

    var meta = document.getElementById('appMeta');
    var isAuth = meta && meta.dataset.auth === '1';

    function token() {
        var el = document.querySelector('#__afToken input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    // ---------- Toast nhỏ phía dưới ----------
    function miniToast(text, icon) {
        var t = document.createElement('div');
        t.className = 'mini-toast';
        t.innerHTML = '<i class="bi ' + (icon || 'bi-check-circle-fill') + '"></i><span></span>';
        t.querySelector('span').textContent = text;
        document.body.appendChild(t);
        requestAnimationFrame(function () { t.classList.add('show'); });
        setTimeout(function () { t.classList.remove('show'); setTimeout(function () { t.remove(); }, 300); }, 2400);
    }
    window.sbToast = miniToast;

    // ---------- Header & back-to-top ----------
    var header = document.querySelector('.site-header');
    var toTop = document.getElementById('backToTop');
    function onScroll() {
        var y = window.scrollY || document.documentElement.scrollTop;
        if (header) header.classList.toggle('is-scrolled', y > 8);
        if (toTop) toTop.classList.toggle('show', y > 600);
    }
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
    if (toTop) toTop.addEventListener('click', function () { window.scrollTo({ top: 0, behavior: 'smooth' }); });

    // ---------- Reveal on scroll ----------
    var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var revealEls = document.querySelectorAll('[data-reveal]');
    if ('IntersectionObserver' in window && !reduceMotion) {
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) {
                if (e.isIntersecting) { e.target.classList.add('is-visible'); io.unobserve(e.target); }
            });
        }, { rootMargin: '0px 0px -40px 0px' });
        revealEls.forEach(function (el, i) {
            el.style.transitionDelay = Math.min(i % 6, 5) * 50 + 'ms';
            io.observe(el);
        });
    } else {
        revealEls.forEach(function (el) { el.classList.add('is-visible'); });
    }

    // ---------- Hộp xác nhận dùng chung ----------
    var confirmModalEl = document.getElementById('appConfirmModal');
    var pendingForm = null;
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        var msg = form.getAttribute('data-confirm-message');
        if (msg && form.dataset.confirmed !== '1' && confirmModalEl && window.bootstrap) {
            e.preventDefault();
            e.stopImmediatePropagation();
            pendingForm = form;
            document.getElementById('appConfirmText').textContent = msg;
            bootstrap.Modal.getOrCreateInstance(confirmModalEl).show();
            return;
        }
        // Trạng thái đang xử lý cho nút submit (bỏ qua form GET lọc/tìm kiếm)
        if (e.defaultPrevented || (form.method || 'get').toLowerCase() !== 'post' || form.hasAttribute('data-no-loading')) return;
        if (window.jQuery && jQuery(form).data('validator') && !jQuery(form).valid()) return;
        var btn = e.submitter || form.querySelector('button[type="submit"], button:not([type])');
        if (btn && !btn.classList.contains('is-loading')) {
            setTimeout(function () {
                if (e.defaultPrevented) return;
                btn.classList.add('is-loading');
                btn.setAttribute('aria-busy', 'true');
            }, 0);
        }
    }, true);
    var okBtn = document.getElementById('appConfirmOk');
    if (okBtn) okBtn.addEventListener('click', function () {
        if (!pendingForm) return;
        pendingForm.dataset.confirmed = '1';
        bootstrap.Modal.getInstance(confirmModalEl).hide();
        if (pendingForm.requestSubmit) pendingForm.requestSubmit(); else pendingForm.submit();
        pendingForm = null;
    });
    // Khi quay lại trang bằng nút Back, gỡ trạng thái loading còn sót
    window.addEventListener('pageshow', function () {
        document.querySelectorAll('.is-loading').forEach(function (b) { b.classList.remove('is-loading'); b.removeAttribute('aria-busy'); });
        document.querySelectorAll('form[data-confirmed]').forEach(function (f) { delete f.dataset.confirmed; });
    });

    // ---------- Sân yêu thích ----------
    var favButtons = document.querySelectorAll('[data-fav-id]');
    function paintFav(id, on) {
        document.querySelectorAll('[data-fav-id="' + id + '"]').forEach(function (b) {
            b.classList.toggle('is-active', on);
            var i = b.querySelector('i');
            if (i) i.className = on ? 'bi bi-heart-fill' : 'bi bi-heart';
            var label = b.querySelector('span');
            if (label) label.textContent = on ? 'Đã lưu' : 'Lưu';
            b.setAttribute('aria-pressed', on ? 'true' : 'false');
            b.title = on ? 'Bỏ khỏi yêu thích' : 'Lưu vào yêu thích';
        });
    }
    if (favButtons.length && isAuth && meta.dataset.favIdsUrl) {
        fetch(meta.dataset.favIdsUrl, { credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (ids) { (ids || []).forEach(function (id) { paintFav(id, true); }); })
            .catch(function () { });
    }
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-fav-id]');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();
        if (!isAuth) {
            window.location.href = meta.dataset.loginUrl + '?returnUrl=' + encodeURIComponent(location.pathname + location.search);
            return;
        }
        var id = btn.getAttribute('data-fav-id');
        btn.classList.add('is-pop');
        setTimeout(function () { btn.classList.remove('is-pop'); }, 350);
        var body = new URLSearchParams();
        body.append('facilityId', id);
        body.append('__RequestVerificationToken', token());
        fetch(meta.dataset.favToggleUrl, {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: body
        })
            .then(function (r) { if (!r.ok) throw new Error(); return r.json(); })
            .then(function (d) {
                paintFav(id, d.isFavorite);
                miniToast(d.isFavorite ? 'Đã lưu vào Sân yêu thích' : 'Đã bỏ khỏi Sân yêu thích', d.isFavorite ? 'bi-heart-fill' : 'bi-heart');
                // Trên trang "Sân yêu thích": ẩn thẻ vừa bỏ
                var page = document.querySelector('[data-fav-page]');
                if (page && !d.isFavorite) {
                    var col = btn.closest('[class*="col-"]');
                    if (col) { col.style.transition = 'opacity .25s, transform .25s'; col.style.opacity = '0'; col.style.transform = 'scale(.96)'; setTimeout(function () { col.remove(); }, 260); }
                    var total = document.getElementById('favTotal');
                    if (total) total.textContent = d.count;
                }
            })
            .catch(function () { miniToast('Không thể cập nhật, vui lòng thử lại', 'bi-exclamation-circle-fill'); });
    });

    // ---------- Chia sẻ link ----------
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-share]');
        if (!btn) return;
        var url = btn.getAttribute('data-share-url') || location.href;
        var title = btn.getAttribute('data-share-title') || document.title;
        if (navigator.share) {
            navigator.share({ title: title, url: url }).catch(function () { });
        } else if (navigator.clipboard) {
            navigator.clipboard.writeText(url).then(function () { miniToast('Đã sao chép liên kết'); });
        } else {
            window.prompt('Sao chép liên kết:', url);
        }
    });

    // ---------- Sân đã xem gần đây (lưu trên trình duyệt) ----------
    var RECENT_KEY = 'sb.recent';
    function readRecent() { try { return JSON.parse(localStorage.getItem(RECENT_KEY) || '[]'); } catch (e) { return []; } }
    var track = document.querySelector('[data-track-recent]');
    if (track) {
        var item = {
            id: track.dataset.venueId, name: track.dataset.venueName, img: track.dataset.venueImg,
            addr: track.dataset.venueAddr, price: track.dataset.venuePrice, sport: track.dataset.venueSport
        };
        var list = readRecent().filter(function (x) { return x.id !== item.id; });
        list.unshift(item);
        try { localStorage.setItem(RECENT_KEY, JSON.stringify(list.slice(0, 8))); } catch (e) { }
    }
    var recentBox = document.getElementById('recentVenues');
    if (recentBox) {
        var items = readRecent();
        if (items.length) {
            var row = recentBox.querySelector('[data-recent-list]');
            var base = recentBox.dataset.detailUrl || '/Booking/FacilityDetail/';
            items.slice(0, 4).forEach(function (x) {
                var a = document.createElement('a');
                a.className = 'recent-item';
                a.href = base + encodeURIComponent(x.id);
                var img = document.createElement('img'); img.src = x.img; img.alt = ''; img.loading = 'lazy';
                var body = document.createElement('div'); body.className = 'min-w-0';
                var n = document.createElement('div'); n.className = 'name text-truncate'; n.textContent = x.name;
                var m = document.createElement('div'); m.className = 'meta text-truncate'; m.textContent = x.sport + ' · từ ' + x.price + 'đ/giờ';
                body.appendChild(n); body.appendChild(m);
                a.appendChild(img); a.appendChild(body);
                row.appendChild(a);
            });
            recentBox.hidden = false;
            var clear = recentBox.querySelector('[data-recent-clear]');
            if (clear) clear.addEventListener('click', function () { localStorage.removeItem(RECENT_KEY); recentBox.hidden = true; });
        }
    }

    // ---------- Giao diện sáng / tối ----------
    document.addEventListener('click', function (e) {
        if (!e.target.closest('[data-theme-toggle]')) return;
        var root = document.documentElement;
        var next = root.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
        root.classList.add('theme-switching');
        root.setAttribute('data-bs-theme', next);
        try { localStorage.setItem('sb.theme', next); } catch (err) { }
        setTimeout(function () { root.classList.remove('theme-switching'); }, 50);
        // Biểu đồ Chart.js cần vẽ lại với màu mới
        if (window.Chart && Object.keys(window.Chart.instances || {}).length) location.reload();
        document.dispatchEvent(new CustomEvent('sb:themechange', { detail: next }));
    });

    // ---------- Bootstrap tooltip (nếu có) ----------
    if (window.bootstrap && bootstrap.Tooltip) {
        document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(function (el) { new bootstrap.Tooltip(el); });
    }
})();

// Bộ ảnh sân: bấm ảnh nào thì mở lightbox đúng ảnh đó
(function () {
    var modal = document.getElementById('photoLightbox');
    if (!modal || !window.bootstrap) return;
    modal.addEventListener('show.bs.modal', function (e) {
        var i = parseInt((e.relatedTarget && e.relatedTarget.getAttribute('data-slide')) || '0', 10);
        var car = bootstrap.Carousel.getOrCreateInstance(document.getElementById('photoCarousel'), { interval: false, ride: false });
        car.to(i);
    });
    document.querySelectorAll('.venue-gallery a').forEach(function (a) { a.addEventListener('click', function (e) { e.preventDefault(); }); });
})();
