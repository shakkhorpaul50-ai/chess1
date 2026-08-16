window.App = (function () {
    var hubs = {};

    function connectHub(path) {
        if (hubs[path]) return hubs[path];
        var conn = new signalR.HubConnectionBuilder()
            .withUrl(path)
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .build();
        hubs[path] = conn.start().then(function () { return conn; });
        return hubs[path];
    }

    function gameHub() { return connectHub('/hubs/game'); }
    function chatHub() { return connectHub('/hubs/chat'); }

    function fmtClock(ms) {
        ms = Math.max(0, Math.round(ms));
        var m = Math.floor(ms / 60000);
        var s = Math.floor((ms % 60000) / 1000);
        return (m < 10 ? '0' : '') + m + ':' + (s < 10 ? '0' : '') + s;
    }

    function fmtTime(iso) {
        var d = new Date(iso);
        return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    function toast(message, isError) {
        var div = document.createElement('div');
        div.className = 'toast-msg ' + (isError ? 'toast-error' : '');
        div.textContent = message;
        document.body.appendChild(div);
        setTimeout(function () { div.classList.add('show'); }, 10);
        setTimeout(function () { div.classList.remove('show'); setTimeout(function () { div.remove(); }, 300); }, 3000);
    }

    async function fetchJson(url) {
        var res = await fetch(url);
        if (!res.ok) throw new Error('Request failed: ' + res.status);
        return res.json();
    }

    return {
        myUserId: document.body.dataset.userId || null,
        connectHub: connectHub,
        gameHub: gameHub,
        chatHub: chatHub,
        fmtClock: fmtClock,
        fmtTime: fmtTime,
        toast: toast,
        fetchJson: fetchJson
    };
})();
