// Small helpers used on every page: mobile menu, QR codes, print buttons, confirm dialogs.
(function () {
    var btn = document.querySelector('[data-menu]');
    var nav = document.getElementById('nav');
    if (btn && nav) btn.addEventListener('click', function () { nav.classList.toggle('open'); });

    document.querySelectorAll('[data-qr]').forEach(function (el) {
        if (typeof qrcode !== 'function') return;
        try {
            var qr = qrcode(0, 'M');
            qr.addData(el.getAttribute('data-qr'));
            qr.make();
            el.innerHTML = qr.createSvgTag({ cellSize: 4, margin: 2, scalable: true });
        } catch (e) { /* QR is only a bonus */ }
    });

    document.querySelectorAll('[data-print]').forEach(function (b) {
        b.addEventListener('click', function () { window.print(); });
    });

    document.addEventListener('submit', function (e) {
        var msg = e.target.getAttribute && e.target.getAttribute('data-confirm');
        if (msg && !window.confirm(msg)) e.preventDefault();
    });

    // fill the link field with the full address of the current page
    document.querySelectorAll('[data-fill-url]').forEach(function (el) { el.value = el.getAttribute('data-fill-url'); });
})();
