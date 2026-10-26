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
   * Creates the runtime of one page. `post` delivers a string to the app,
   * and `postFiles`, when the webview has it, a string with File objects;
   * the returned `receive` takes the app's messages. Free of DOM access, so
   * that it can be tested on its own.
   */
  function createRuntime(post, postFiles) {
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
          else p.reject(callError(p.method, errorMessage(p.method, message.e), message.c, message.d));
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

    /**
     * Sees a keydown of the page first, and reports it to the app once the
     * page's listeners had their turn, unless one of them called
     * preventDefault(): it may be the accelerator of a menu. Only keys with
     * Ctrl or Alt and function keys: the others are the page's, and AltGr
     * (Ctrl+Alt on Windows) types characters.
     */
    function keyDown(e) {
      const fn = e.keyCode >= 112 && e.keyCode <= 135;
      if (!(e.ctrlKey || e.altKey || fn) || e.isComposing || MODIFIER_KEYS.has(e.keyCode)) return;
      if (typeof e.getModifierState === "function" && e.getModifierState("AltGraph")) return;
      setTimeout(() => {
        if (e.defaultPrevented) return;
        const m = (e.ctrlKey ? 1 : 0) | (e.shiftKey ? 2 : 0) | (e.altKey ? 4 : 0);
        send({ t: "key", k: token, c: e.keyCode, m });
      });
    }

    /**
     * Lets files dragged over the page be dropped, as the page's own
     * listeners may refuse them later (dropEffect "none"). Seen first, in
     * the capture phase. Other drags are the page's.
     */
    function dragOver(e) {
      if (!hasFiles(e.dataTransfer)) return;
      e.preventDefault();
      e.dataTransfer.dropEffect = "copy";
    }

    /**
     * Sends the files dropped on the page to the app, which reads their
     * paths: the page's own listeners still get the File objects. Keeps the
     * webview from opening the file in place of the page.
     */
    function drop(e) {
      if (!hasFiles(e.dataTransfer)) return;
      e.preventDefault();
      if (!postFiles) return;
      const files = Array.from(e.dataTransfer.files || []);
      if (files.length === 0) return;
      postFiles(JSON.stringify({ t: "drop", k: token, x: e.clientX, y: e.clientY }), files);
    }

    send({ t: "hello", k: token });
    return { runtime: Object.freeze({ call, on }), receive, keyDown, dragOver, drop, token };
  }

  /** Whether a drag carries files from the system, rather than text or an element of the page. */
  function hasFiles(transfer) {
    return !!transfer && Array.from(transfer.types || []).includes("Files");
  }

  /** Shift, Ctrl, Alt and the Windows keys: pressed alone, they are no shortcut. */
  const MODIFIER_KEYS = new Set([16, 17, 18, 91, 92, 93]);

  /**
   * The error a failed call rejects with; the generated client rethrows it as
   * its CallError. Errors of [ServiceError] exceptions have a code and data.
   */
  function callError(method, message, code, data) {
    const err = new Error(message);
    err.name = "CallError";
    err.method = method;
    err.code = typeof code === "string" ? code : null;
    err.data = err.code === null ? null : data;
    return err;
  }

  /**
   * The message of an error reply: a string from the app, else one of the
   * runtime's. String() of any JSON would throw on {"toString":0}, leaving
   * the call pending forever.
   */
  function errorMessage(method, e) {
    return typeof e === "string" ? e : `${method} failed`;
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
  const postFiles =
    typeof webview.postMessageWithAdditionalObjects === "function"
      ? (message, files) => webview.postMessageWithAdditionalObjects(message, files)
      : null; // an older runtime: drops reach the page only
  const { runtime, receive, keyDown, dragOver, drop } = createRuntime((message) => webview.postMessage(message), postFiles);
  webview.addEventListener("message", (e) => receive(e.data));
  root.addEventListener("keydown", keyDown, true);
  root.addEventListener("dragover", dragOver, true);
  root.addEventListener("drop", drop, true);
  Object.defineProperty(root, "oriel", { value: runtime, enumerable: true });
})(globalThis);
