window.Stockfish = (function () {
    var worker = null;
    var readyPromise = null;
    var currentCb = null;
    var level = 12;

    function load() {
        if (readyPromise) return readyPromise;
        readyPromise = (async function () {
            var urls = [
                'https://cdn.jsdelivr.net/npm/stockfish.js@10.0.2/stockfish.js',
                '/stockfish/stockfish.js'
            ];
            var text = null;
            for (var i = 0; i < urls.length; i++) {
                try {
                    var res = await fetch(urls[i]);
                    if (res.ok) { text = await res.text(); break; }
                } catch (e) { /* try next */ }
            }
            if (!text) throw new Error('Stockfish could not be loaded. Check your connection.');

            var blob = new Blob([text], { type: 'application/javascript' });
            worker = new Worker(URL.createObjectURL(blob));

            worker.onmessage = function (e) {
                var line = typeof e.data === 'string' ? e.data : (e.data && e.data.data);
                if (!line) return;
                var parts = line.split(' ');
                if (parts[0] === 'bestmove' && parts[1] && parts[1] !== '(none)' && currentCb) {
                    var cb = currentCb;
                    currentCb = null;
                    cb(parts[1]);
                }
            };

            await send('uci');
            await send('setoption name Skill Level value ' + level);
            await send('isready');
            return worker;
        })();
        return readyPromise;
    }

    function send(cmd) {
        return new Promise(function (resolve, reject) {
            var timeout = setTimeout(function () { resolve(); }, 15000);
            var handler = function (e) {
                var line = typeof e.data === 'string' ? e.data : (e.data && e.data.data);
                if (line && (line.indexOf('uciok') === 0 || line.indexOf('readyok') === 0)) {
                    clearTimeout(timeout);
                    worker.removeEventListener('message', handler);
                    resolve();
                }
            };
            if (cmd === 'uci' || cmd === 'isready') worker.addEventListener('message', handler);
            worker.postMessage(cmd);
        });
    }

    function getBestMove(fen, depth, cb) {
        load().then(function () {
            currentCb = cb;
            worker.postMessage('position fen ' + fen);
            worker.postMessage('go depth ' + (depth || 12));
        }).catch(function (e) {
            cb(null, e);
        });
    }

    return {
        load: load,
        getBestMove: getBestMove,
        setLevel: function (l) { level = l; }
    };
})();
