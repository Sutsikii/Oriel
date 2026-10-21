// The page side of Oriel's IPC. Oriel injects this script at document start
// into the pages of its windows, where it defines `window.oriel`: the object
// the generated TypeScript client calls. docs/protocol.md describes the
// messages it exchanges with the app.
//
// It is plain ES2020 with no build step, so that what runs in the page is
// what is in the repository. In Node (the tests) it exports createRuntime
// instead of touching a page.
(function (root) {
  "use strict";

  /**
   * Creates the runtime of one page. `post` delivers a string to the app;
   * the returned `receive` takes the app's messages. Free of DOM access, so
   * that it can be tested on its own.
   */
  function createRuntime(post) {
    // A token per page: the app drops the work of a page whose token it no
    // longer sees, and replies meant for a previous page are ignored here.
    const token = randomToken();
    const pending = new Map();
    const listeners = new Map();
    let seq = 0;

    const send = (message) => post(JSON.stringify(message));

    /** Calls `method` ("Service.Method") with `args`; resolves with its result. */
    function call(method, args) {
      const id = ++seq;
      return new Promise((resolve, reject) => {
        pending.set(id, { method, resolve, reject });
        try {
          send({ t: "call", id, k: token, m: method, a: args === undefined ? [] : args });
        } catch (err) {
          pending.delete(id);
          reject(err);
        }
      });
    }

    /** Listens to the event `name`; returns a function that stops listening. */
    function on(name, listener) {
      let set = listeners.get(name);
      if (!set) listeners.set(name, (set = new Set()));
      set.add(listener);
      return () => {
        set.delete(listener);
        if (set.size === 0 && listeners.get(name) === set) listeners.delete(name);
      };
    }

    /** Handles a message from the app; ignores what it does not understand. */
    function receive(raw) {
      let message;
      try {
        message = typeof raw === "string" ? JSON.parse(raw) : raw;
      } catch {
        return;
      }
      if (!message || typeof message !== "object") return;
      switch (message.t) {
        case "result":
        case "error": {
          if (message.k !== token) return;
          const p = pending.get(message.id);
          if (!p) return;
          pending.delete(message.id);
          if (message.t === "result") p.resolve(message.v);
          else p.reject(callError(p.method, String(message.e)));
          return;
        }
        case "event": {
          const set = listeners.get(message.n);
          if (!set) return;
          for (const listener of [...set]) {
            try {
              listener(message.v);
            } catch (err) {
              report(err);
            }
          }
          return;
        }
      }
    }

    send({ t: "hello", k: token });
    return { runtime: Object.freeze({ call, on }), receive, token };
  }

  /** The error a failed call rejects with; the generated client rethrows it as its CallError. */
  function callError(method, message) {
    const err = new Error(message);
    err.name = "CallError";
    err.method = method;
    return err;
  }

  /** Reports an error thrown by page code without stopping the runtime. */
  function report(err) {
    setTimeout(() => {
      throw err;
    });
  }

  function randomToken() {
    const bytes = new Uint8Array(12);
    root.crypto.getRandomValues(bytes);
    return Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
  }

  if (typeof module === "object" && module.exports) {
    module.exports = { createRuntime };
    return;
  }

  // Only the top document of an Oriel window talks to the app.
  const webview = root.chrome && root.chrome.webview;
  if (!webview || root.oriel || root.top !== root) return;
  const { runtime, receive } = createRuntime((message) => webview.postMessage(message));
  webview.addEventListener("message", (e) => receive(e.data));
  Object.defineProperty(root, "oriel", { value: runtime, enumerable: true });
})(globalThis);
