// Display board: speaks "Token G M 12, please go to Room 1" when a new call appears.
// Browsers only allow sound after a click, so the sound button must be pressed once.
(function () {
    var root = document.getElementById('board');
    var btn = document.getElementById('sound-btn');
    var clock = document.getElementById('board-clock');
    if (!root) return;

    var enabled = false;
    var seen = {};
    var first = true;

    function scan() {
        var cards = root.querySelectorAll('[data-stamp]');
        var toSay = [];
        cards.forEach(function (c) {
            var id = c.getAttribute('data-dept');
            var stamp = c.getAttribute('data-stamp');
            if (!first && seen[id] !== undefined && seen[id] !== stamp && c.getAttribute('data-speak')) {
                toSay.push(c.getAttribute('data-speak'));
                c.classList.add('flash-call');
            }
            seen[id] = stamp;
        });
        first = false;
        if (enabled && 'speechSynthesis' in window) {
            toSay.forEach(function (t) {
                var u = new SpeechSynthesisUtterance(t);
                u.rate = 0.9;
                window.speechSynthesis.speak(u);
            });
        }
    }

    root.addEventListener('live:updated', scan);
    scan();

    if (btn) {
        btn.addEventListener('click', function () {
            enabled = !enabled;
            btn.textContent = enabled ? '🔊 Sound on' : '🔇 Sound off';
            btn.classList.toggle('on', enabled);
            if (enabled && 'speechSynthesis' in window) {
                window.speechSynthesis.speak(new SpeechSynthesisUtterance('Sound is on'));
            }
        });
    }

    function tick() {
        if (!clock) return;
        var d = new Date();
        clock.textContent = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }
    tick();
    setInterval(tick, 10000);
})();
