const STATIC_PREFIXES = ['/js/', '/css/', '/lib/', '/stockfish/', '/favicon.ico'];

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const origin = env.ORIGIN || 'https://chess-app.onrender.com';

    const isUpgrade = request.headers.get('Upgrade') === 'websocket';

    let target;
    if (!isUpgrade && env.ASSETS_ORIGIN && STATIC_PREFIXES.some((p) => url.pathname.startsWith(p))) {
      target = env.ASSETS_ORIGIN;
    } else {
      target = origin;
    }

    const targetUrl = new URL(target);
    targetUrl.pathname = url.pathname;
    targetUrl.search = url.search;

    const headers = new Headers(request.headers);
    headers.set('X-Forwarded-Proto', 'https');
    headers.set('X-Forwarded-For', request.headers.get('CF-Connecting-IP') || '');
    headers.set('X-Real-IP', request.headers.get('CF-Connecting-IP') || '');

    const proxied = new Request(targetUrl, {
      method: request.method,
      headers,
      body: request.method === 'GET' || request.method === 'HEAD' ? undefined : request.body,
      redirect: 'follow',
    });

    const response = await fetch(proxied);

    if (isUpgrade && response.webSocket) {
      return new Response(null, {
        status: 101,
        webSocket: response.webSocket,
      });
    }

    return response;
  },
};