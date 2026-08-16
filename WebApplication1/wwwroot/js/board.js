window.ChessBoard = (function () {
    var FILES = ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'];
    var GLYPHS = { k: '\u265A', q: '\u265B', r: '\u265C', b: '\u265D', n: '\u265E', p: '\u265F' };

    function create(container, opts) {
        opts = opts || {};
        var orientation = opts.orientation || 'white';
        var fen = null;
        var selected = null;
        var legalTargets = [];
        var lastMove = null;
        var interactive = opts.interactive !== false;
        var onSquareClick = opts.onSquareClick || function () { };
        var cells = [];

        container.classList.add('chessboard');
        container.innerHTML = '';

        for (var i = 0; i < 64; i++) {
            var cell = document.createElement('div');
            cell.className = 'square';
            cell.addEventListener('click', function () {
                if (!interactive) return;
                onSquareClick(this.dataset.square);
            });
            container.appendChild(cell);
            cells.push(cell);
        }

        function displayToSquare(r, c) {
            if (orientation === 'white') return FILES[c] + (8 - r);
            return FILES[7 - c] + (r + 1);
        }

        function render() {
            var pieceMap = {};
            if (fen) {
                var rows = fen.split(' ')[0].split('/');
                for (var r = 0; r < 8; r++) {
                    var file = 0;
                    for (var k = 0; k < rows[r].length; k++) {
                        var ch = rows[r][k];
                        if (ch >= '1' && ch <= '8') { file += parseInt(ch); continue; }
                        pieceMap[FILES[file] + (8 - r)] = ch;
                        file++;
                    }
                }
            }

            for (var r = 0; r < 8; r++) {
                for (var c = 0; c < 8; c++) {
                    var sq = displayToSquare(r, c);
                    var cell = cells[r * 8 + c];
                    var ch = pieceMap[sq] || '';
                    var isWhite = ch === ch.toUpperCase();

                    cell.className = 'square' + ((r + c) % 2 === 0 ? ' light' : ' dark');
                    cell.dataset.square = sq;
                    cell.textContent = '';

                    if (lastMove && (sq === lastMove.from || sq === lastMove.to))
                        cell.classList.add('last-move');
                    if (selected && sq === selected)
                        cell.classList.add('selected');

                    if (ch) {
                        cell.textContent = GLYPHS[ch.toLowerCase()];
                        cell.classList.add('has-piece');
                        cell.classList.add(isWhite ? 'piece-white' : 'piece-black');
                    }

                    if (legalTargets.indexOf(sq) >= 0) {
                        var dot = document.createElement('span');
                        dot.className = ch ? 'legal-ring' : 'legal-dot';
                        cell.appendChild(dot);
                    }

                    if (r === 7 && orientation === 'white') {
                        var f = document.createElement('span');
                        f.className = 'coord-file';
                        f.textContent = FILES[c];
                        cell.appendChild(f);
                    }
                    if (r === 0 && orientation === 'white') {
                        var rk = document.createElement('span');
                        rk.className = 'coord-rank';
                        rk.textContent = 8 - c === 0 ? '' : 8 - c;
                        cell.appendChild(rk);
                    }
                    if (r === 0 && orientation === 'black') {
                        var f2 = document.createElement('span');
                        f2.className = 'coord-file';
                        f2.textContent = FILES[7 - c];
                        cell.appendChild(f2);
                    }
                    if (r === 7 && orientation === 'black') {
                        var rk2 = document.createElement('span');
                        rk2.className = 'coord-rank';
                        rk2.textContent = c + 1;
                        cell.appendChild(rk2);
                    }
                }
            }
        }

        return {
            setFen: function (f) { fen = f; render(); },
            setOrientation: function (o) { orientation = o; render(); },
            setLastMove: function (m) { lastMove = m; render(); },
            setHighlights: function (targets) { legalTargets = targets || []; render(); },
            selectSquare: function (sq) { selected = sq; render(); },
            clearSelection: function () { selected = null; legalTargets = []; render(); },
            setInteractive: function (v) { interactive = v; },
            getOrientation: function () { return orientation; }
        };
    }

    return { create: create };
})();
