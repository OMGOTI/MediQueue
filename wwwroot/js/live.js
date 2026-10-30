// Live refresh. Any element with data-live-url is reloaded
//   - at once when the server says "queueChanged" (SignalR), and
//   - every 15 seconds anyway (so it still works if the live connection is blocked).
// Optional data-live-dept="3" limits the instant refresh to that department.
(function () {
    var targets = Array.prototype.slice.call(document.querySelectorAll('[data-live-url]'));
    if (!targets.length) return;

    var busy = new WeakSet();

    function refresh(el) {
        if (busy.has(el)) return;
        // do not replace the page while someone is typing in it
        var a = document.activeElement;
        if (a && el.contains(a) && /^(INPUT|SELECT|TEXTAREA)$/.test(a.tagName)) return;
        busy.add(el);
        fetch(el.getAttribute('data-live-url'), { headers: { 'X-Requested-With': 'fetch' }, cache: 'no-store' })
            .then(function (r) { if (r.status === 401 || r.status === 403) { location.reload(); return null; } return r.ok ? r.text() : null; })
            .then(function (html) {
                if (html === null) return;
                el.innerHTML = html;
                el.dispatchEvent(new CustomEvent('live:updated', { bubbles: true }));
            })
            .catch(function () { /* offline for a moment - try again next time */ })
            .then(function () { busy.delete(el); });
    }

    function refreshAll(dept) {
        targets.forEach(function (el) {
            var d = el.getAttribute('data-live-dept');
            if (dept == null || !d || String(d) === String(dept)) refresh(el);
        });
    }

    setInterval(function () { if (!document.hidden) refreshAll(null); }, 15000);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) refreshAll(null); });

    if (typeof signalR !== 'undefined') {
        var conn = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/queue')
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .build();
        conn.on('queueChanged', function (dept) { refreshAll(dept); });
        conn.onreconnected(function () { refreshAll(null); });
        conn.start().catch(function () { /* polling keeps working */ });
        window.__queueConn = conn;
    }
})();
