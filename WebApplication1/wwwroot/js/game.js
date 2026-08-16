window.initGamePage = (function () {
    var gameId, myId;
    var mode = 'spectator';      // 'player' | 'bot' | 'spectator'
    var myColor = null;          // 'w' | 'b' when player/bot
    var botColor = null;         // 'w' | 'b' for bot games
    var ended = false;
    var gameStarted = false;
    var drawOfferedBy = null;
    var chess = new Chess();
    var dto = null;
    var board = null;
    var hub = null;
    var chatHub = null;
    var clockSync = null;
    var clockTimer = null;
    var selectedSquare = null;
    var promotionCb = null;
    var pollTimer = null;

    function el(id) { return document.getElementById(id); }

    function setStatus(text, cls) {
        var s = el('gameStatus');
        if (s) { s.textContent = text; s.className = 'badge ' + (cls || 'bg-secondary'); }
    }

    function playerLabel(p, botFallback) {
        if (p) return p.username + ' (' + p.elo + ')';
        return botFallback || '(waiting)';
    }

    function renderHeader() {
        var w = el('whiteName'), b = el('blackName');
        if (w) w.textContent = playerLabel(dto ? dto.white : null, dto && dto.isVsBot ? 'Stockfish' : 'White');
        if (b) b.textContent = playerLabel(dto ? dto.black : null, dto && dto.isVsBot ? 'Stockfish' : 'Black');
    }

    function applyMovesList() {
        var container = el('moveList');
        if (!container) return;
        var rows = [];
        for (var i = 0; i < dto.moves.length; i++) {
            var num = Math.floor(i / 2) + 1;
            if (i % 2 === 0) rows.push('<tr><td>' + num + '.</td>');
            rows.push('<td>' + dto.moves[i] + '</td>');
            if (i % 2 === 1) rows.push('</tr>');
        }
        if (dto.moves.length % 2 === 1) rows.push('<td></td></tr>');
        container.innerHTML = rows.join('') ||
            '<div class="text-muted text-center py-2">No moves yet</div>';
        container.scrollTop = container.scrollHeight;
    }

    function setClocks(whiteMs, blackMs, mover, nowMs) {
        clockSync = { whiteMs: whiteMs, blackMs: blackMs, mover: mover, nowMs: nowMs || Date.now() };
    }

    function displayClocks() {
        if (!clockSync) return;
        var now = Date.now();
        var elapsed = now - clockSync.nowMs;
        var whiteMs = clockSync.mover === 'White' ? Math.max(0, clockSync.whiteMs - elapsed) : clockSync.whiteMs;
        var blackMs = clockSync.mover === 'Black' ? Math.max(0, clockSync.blackMs - elapsed) : clockSync.blackMs;

        var w = el('clockWhite'), b = el('clockBlack');
        if (w) { w.textContent = App.fmtClock(whiteMs); w.classList.toggle('active-clk', clockSync.mover === 'White' && !ended); }
        if (b) { b.textContent = App.fmtClock(blackMs); b.classList.toggle('active-clk', clockSync.mover === 'Black' && !ended); }
    }

    function startClockTimer() {
        if (!clockTimer) clockTimer = setInterval(displayClocks, 250);
    }

    function myTurn() {
        if (!gameStarted || ended) return false;
        if (mode === 'spectator') return false;
        return chess.turn() === myColor;
    }

    function botThinks() {
        return mode === 'bot' && !ended && chess.turn() === botColor;
    }

    function onSquareClick(square) {
        if (promotionCb) return;
        if (!myTurn()) return;

        var piece = chess.get(square);
        var isMyPiece = piece && piece.color === myColor;

        if (selectedSquare) {
            var targets = chess.moves({ square: selectedSquare, verbose: true }).map(function (m) { return m.to; });
            if (targets.indexOf(square) >= 0) {
                attemptMove(selectedSquare, square);
                return;
            }
        }

        if (isMyPiece) {
            selectedSquare = square;
            board.selectSquare(square);
            board.setHighlights(chess.moves({ square: square, verbose: true }).map(function (m) { return m.to; }));
        } else {
            board.clearSelection();
            selectedSquare = null;
        }
    }

    function attemptMove(from, to) {
        var moves = chess.moves({ square: from, verbose: true }).filter(function (m) { return m.to === to; });
        var promos = {};
        moves.forEach(function (m) { if (m.promotion) promos[m.promotion] = true; });
        var promoKeys = Object.keys(promos);

        if (promoKeys.length === 0) { sendMove(from, to, null); return; }
        if (promoKeys.length === 1) { sendMove(from, to, promoKeys[0]); return; }

        promotionCb = function (p) { sendMove(from, to, p); };
        var modal = el('promoModal');
        if (modal) {
            modal.querySelectorAll('[data-piece]').forEach(function (btn) {
                btn.onclick = function () {
                    var cb = promotionCb; promotionCb = null;
                    bootstrap.Modal.getInstance(modal).hide();
                    cb && cb(btn.dataset.piece);
                };
            });
            bootstrap.Modal.getOrCreateInstance(modal).show();
        } else {
            var fallbackCb = promotionCb;
            promotionCb = null;
            fallbackCb('q');
        }
    }

    async function sendMove(from, to, promotion) {
        try {
            await hub.invoke('PlayMove', gameId, from, to, promotion);
        } catch (e) {
            App.toast(e.message || e, true);
        } finally {
            selectedSquare = null;
            board.clearSelection();
        }
    }

    function refreshFromDto(g) {
        dto = g;
        if (!dto) return;
        chess = new Chess();
        (dto.moves || []).forEach(function (san) {
            try { chess.move(san); } catch (e) { }
        });
        board.setFen(dto.fen);
        var history = chess.history({ verbose: true });
        if (history.length) {
            var last = history[history.length - 1];
            board.setLastMove({ from: last.from, to: last.to });
        }
        renderHeader();
        applyMovesList();
        setClocks(dto.whiteMsLeft, dto.blackMsLeft, dto.whoseTurn);
        updateControls();
        updateTurnHint();
    }

    async function onMovePlayed(ev) {
        chess.load(ev.fen);
        if (dto) dto.moves.push(ev.san);
        applyMovesList();
        board.setLastMove({ from: ev.from, to: ev.to });
        board.setFen(ev.fen);
        board.clearSelection();
        selectedSquare = null;
        setClocks(ev.whiteMsLeft, ev.blackMsLeft, ev.whoseTurn);
        startClockTimer();
        updateTurnHint();

        if (botThinks()) {
            setStatus('Stockfish is thinking...', 'bg-info');
            botMove();
        } else if (!ended) {
            setStatus(myTurn() ? 'Your turn' : (mode === 'spectator' ? 'Watching...' : 'Opponent to move'), myTurn() ? 'bg-success' : 'bg-secondary');
        }
    }

    function updateTurnHint() {
        var th = el('turnHint');
        if (th) th.textContent = ended ? '' : (chess.turn() === 'w' ? 'White to move' : 'Black to move');
    }

    function botMove() {
        window.Stockfish.getBestMove(chess.fen(), 12, function (move, err) {
            if (err) { App.toast('Stockfish error: ' + err.message, true); setStatus('Your turn', 'bg-success'); return; }
            if (!move) return;
            var from = move.slice(0, 2), to = move.slice(2, 4), promo = move.length > 4 ? move[4] : null;
            hub.invoke('PlayMove', gameId, from, to, promo, true).catch(function (e) {
                App.toast('Bot move failed: ' + (e.message || e), true);
            });
        });
    }

    async function onGameStarted(g) {
        gameStarted = true;
        if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
        refreshFromDto(g);
        board.setOrientation(myColor === 'b' ? 'black' : 'white');
        setStatus('Game started', 'bg-success');
        if (botThinks()) setTimeout(botMove, 800);
    }

    function onGameEnded(ev) {
        ended = true;
        if (dto) { dto.status = 'Ended'; dto.result = ev.result; dto.resultReason = ev.reason; }
        setStatus('Game over', 'bg-secondary');
        updateTurnHint();
        el('btnResign') && (el('btnResign').style.display = 'none');
        el('btnDraw') && (el('btnDraw').style.display = 'none');
        el('btnAcceptDraw') && (el('btnAcceptDraw').style.display = 'none');
        el('btnDeclineDraw') && (el('btnDeclineDraw').style.display = 'none');
        var da = el('drawOfferArea');
        if (da) da.style.display = 'none';
        var isPlayer = mode === 'player' || mode === 'bot';
        el('btnRematch') && (el('btnRematch').style.display = isPlayer ? '' : 'none');
        el('btnAcceptRematch') && (el('btnAcceptRematch').style.display = 'none');
        el('btnDeclineRematch') && (el('btnDeclineRematch').style.display = 'none');

        var modal = el('gameOverModal');
        if (modal) {
            var text = ev.result === 'Draw' ? 'Draw' :
                ((ev.result === 'WhiteWon' ? (dto && dto.white ? dto.white.username : 'Stockfish') : (dto && dto.black ? dto.black.username : 'Stockfish')) + ' wins');
            el('overResult').textContent = text;
            el('overReason').textContent = ev.reason || '';
            var msg = mode === 'spectator' ? 'Thanks for watching!' :
                (ev.result === 'Draw' ? 'It is a draw.' :
                    ((ev.result === 'WhiteWon' && myColor === 'w') || (ev.result === 'BlackWon' && myColor === 'b') ? 'You won!' : 'You lost.'));
            el('overMsg').textContent = msg;
            bootstrap.Modal.getOrCreateInstance(modal).show();
        }
    }

    function updateControls() {
        var isPlayer = mode === 'player' || mode === 'bot';
        var res = el('btnResign'), dr = el('btnDraw');
        if (res) res.style.display = (isPlayer && gameStarted && !ended) ? '' : 'none';
        if (dr) dr.style.display = (isPlayer && gameStarted && !ended) ? '' : 'none';

        var waiting = dto && dto.status === 'Waiting';
        var leave = el('btnCancel');
        if (leave) leave.style.display = (waiting && dto.white && dto.white.id === myId) ? '' : 'none';
    }

    function initChat() {
        var box = el('chatBox'), input = el('chatInput'), sendBtn = el('chatSend');
        if (!box) return;

        App.fetchJson('/Game/GameChatData?id=' + gameId).then(function (msgs) {
            box.innerHTML = '';
            msgs.forEach(appendChat);
        }).catch(function () { });

        if (chatHub) chatHub.on('GameMessageReceived', function (m) { appendChat(m); });

        function appendChat(m) {
            var div = document.createElement('div');
            div.className = 'chat-msg' + (m.senderId === myId ? ' mine' : '');
            div.innerHTML = '<b>' + escapeHtml(m.senderName || '?') + '</b> ' + escapeHtml(m.content) +
                '<span class="chat-time">' + App.fmtTime(m.sentAt) + '</span>';
            box.appendChild(div);
            box.scrollTop = box.scrollHeight;
        }

        function send() {
            var text = input.value.trim();
            if (!text) return;
            chatHub.invoke('SendGameMessage', gameId, text).catch(function (e) { App.toast(e.message || e, true); });
            input.value = '';
        }
        if (sendBtn) sendBtn.onclick = send;
        if (input) input.addEventListener('keydown', function (ev) { if (ev.key === 'Enter') send(); });
    }

    function escapeHtml(s) {
        var d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    }

    async function init() {
        gameId = new URLSearchParams(location.search).get('id');
        myId = App.myUserId;
        if (!gameId) { App.toast('Missing game id', true); return; }

        hub = await App.gameHub();
        chatHub = await App.chatHub();

        hub.on('MovePlayed', onMovePlayed);
        hub.on('GameStarted', onGameStarted);
        hub.on('GameEnded', onGameEnded);
        hub.on('DrawOffered', function (id2, by) {
            drawOfferedBy = by;
            if (by === myId) {
                setStatus('Draw offered, waiting for opponent...', 'bg-info');
            } else {
                el('btnAcceptDraw') && (el('btnAcceptDraw').style.display = '');
                el('btnDeclineDraw') && (el('btnDeclineDraw').style.display = '');
                setStatus('Opponent offers a draw', 'bg-warning');
            }
        });
        hub.on('DrawDeclined', function () {
            drawOfferedBy = null;
            el('btnAcceptDraw') && (el('btnAcceptDraw').style.display = 'none');
            el('btnDeclineDraw') && (el('btnDeclineDraw').style.display = 'none');
            setStatus(myTurn() ? 'Your turn' : 'Game continues', 'bg-secondary');
        });
        hub.on('ClockSync', function (c) {
            setClocks(c.whiteMsLeft, c.blackMsLeft, c.whoseTurn, c.nowMs);
        });
        hub.on('RematchOffered', function (id2, by) {
            if (by === myId) {
                setStatus('Rematch offered, waiting for opponent...', 'bg-info');
            } else {
                el('btnAcceptRematch') && (el('btnAcceptRematch').style.display = '');
                el('btnDeclineRematch') && (el('btnDeclineRematch').style.display = '');
                setStatus('Opponent offers a rematch', 'bg-warning');
            }
        });
        hub.on('RematchDeclined', function () {
            el('btnAcceptRematch') && (el('btnAcceptRematch').style.display = 'none');
            el('btnDeclineRematch') && (el('btnDeclineRematch').style.display = 'none');
            setStatus('Rematch declined', 'bg-secondary');
        });
        hub.on('RematchStarted', function (newId) {
            if (mode === 'player' || mode === 'bot') location.href = '/Game/Play?id=' + newId;
        });

        board = ChessBoard.create(el('board'), { onSquareClick: onSquareClick });

        dto = await hub.invoke('GetGame', gameId);
        if (!dto) { App.toast('Game not found', true); return; }

        if (dto.isVsBot) {
            mode = 'bot';
            myColor = dto.white && dto.white.id === myId ? 'w' : 'b';
            botColor = myColor === 'w' ? 'b' : 'w';
        } else if (dto.white && dto.white.id === myId) {
            mode = 'player'; myColor = 'w';
        } else if (dto.black && dto.black.id === myId) {
            mode = 'player'; myColor = 'b';
        } else {
            mode = 'spectator';
        }

        board.setOrientation(myColor === 'b' ? 'black' : 'white');
        refreshFromDto(dto);
        initChat();
        startClockTimer();

        if (dto.status === 'Waiting') {
            gameStarted = false;
            if (dto.white && dto.white.id === myId) {
                setStatus('Waiting for an opponent...', 'bg-info');
                startPolling();
            } else {
                try {
                    var joined = await hub.invoke('JoinGame', gameId);
                    onGameStarted(joined);
                } catch (e) {
                    App.toast(e.message || e, true);
                    setStatus('Could not join game', 'bg-danger');
                }
            }
        } else if (mode === 'player' || mode === 'bot') {
            try {
                var st = await hub.invoke('JoinGame', gameId);
                gameStarted = st.status === 'InProgress' ? true : gameStarted;
                if (gameStarted) { setStatus('Game in progress', 'bg-secondary'); startClockTimer(); }
            } catch (e) { App.toast(e.message || e, true); }
        } else {
            await hub.invoke('Spectate', gameId).catch(function () { });
            gameStarted = dto.status === 'InProgress';
            if (gameStarted) { setStatus('Watching...', 'bg-secondary'); startClockTimer(); }
        }

        if (gameStarted && mode === 'bot' && botThinks()) setTimeout(botMove, 800);
    }

    function startPolling() {
        pollTimer = setInterval(async function () {
            if (gameStarted) return;
            var s = await hub.invoke('GetGame', gameId).catch(function () { return null; });
            if (s && s.status !== 'Waiting') {
                var joined = null;
                try { joined = await hub.invoke('JoinGame', gameId); } catch (e) { }
                onGameStarted(joined || s);
                setStatus('Game started', 'bg-success');
            }
        }, 2000);
    }

    document.addEventListener('DOMContentLoaded', init);
    return {};
})();