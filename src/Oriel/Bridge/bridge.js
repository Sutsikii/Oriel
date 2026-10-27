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
   * the returned `receive` takes the app's messages. `frame` runs a function
   * at the next frame (requestAnimationFrame in a page). Free of DOM access,
   * so that it can be tested on its own.
   */
  function createRuntime(post, postFiles, frame) {
    // A token per page: the app drops the work of a page whose token it no
    // longer sees, and replies meant for a previous page are ignored here.
    const token = randomToken();
    const pending = new Map();
    const listeners = new Map();
    // The coalesced events taken since the last frame: the app sends their
    // next payloads once the page acknowledges them, so a busy or hidden
    // page gets the latest payload rather than a queue of them.
    const acks = new Set();
    const nextFrame = frame || ((f) => setTimeout(f, 16));
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

    /**
     * Calls `method`, which returns a stream, with `args`; returns an async
     * iterable of its items. The call starts when iteration does. The app
     * sends no more items than the page is ready for, and stops the stream
     * when the page stops iterating.
     */
    function stream(method, args) {
      return { [Symbol.asyncIterator]: () => iterate(method, args) };
    }

    function iterate(method, args) {
      const id = ++seq;
      const items = []; // arrived, not taken yet
      const waiting = []; // next() calls waiting for an item
      let ended = false; // nothing more arrives
      let failure = null; // the error next() rejects with once the items are taken
      let taken = 0; // items taken since the last pull

      function end(err) {
        if (ended) return;
        ended = true;
        pending.delete(id);
        if (waiting.length === 0) failure = err;
        for (const w of waiting.splice(0)) {
          if (err) w.reject(err);
          else w.resolve({ value: undefined, done: true });
          err = null; // one rejection, then the end
        }
      }

      // Grants the items taken once half the window is, so that the app
      // keeps a few on their way.
      function took() {
        if (++taken < STREAM_WINDOW / 2 || ended) return;
        send({ t: "pull", id, k: token, n: taken });
        taken = 0;
      }

      pending.set(id, {
        method,
        item(v) {
          if (ended) return;
          const w = waiting.shift();
          if (w) {
            w.resolve({ value: v, done: false });
            took();
          } else {
            items.push(v);
          }
        },
        resolve: () => end(null),
        reject: (err) => end(err),
      });
      try {
        send({ t: "call", id, k: token, m: method, a: args === undefined ? [] : args, w: STREAM_WINDOW });
      } catch (err) {
        end(err);
      }

      return {
        next() {
          if (items.length > 0) {
            const value = items.shift();
            took();
            return Promise.resolve({ value, done: false });
          }
          if (ended) {
            const err = failure;
            failure = null;
            return err ? Promise.reject(err) : Promise.resolve({ value: undefined, done: true });
          }
          return new Promise((resolve, reject) => waiting.push({ resolve, reject }));
        },
        return() {
          if (!ended) {
            send({ t: "cancel", id, k: token });
            end(null);
          }
          items.length = 0;
          failure = null;
          return Promise.resolve({ value: undefined, done: true });
        },
        [Symbol.asyncIterator]() {
          return this;
        },
      };
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

    /**
     * Handles a message from the app; ignores what it does not understand.
     * `buffer` is the ArrayBuffer the app shared with it, if any.
     */
    function receive(raw, buffer) {
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
          if (message.t === "error") p.reject(callError(p.method, errorMessage(p.method, message.e), message.c, message.d));
          else if (typeof message.b === "string") binary(p, message, buffer);
          else p.resolve(message.v);
          return;
        }
        case "item": {
          if (message.k !== token) return;
          const p = pending.get(message.id);
          if (p && p.item) p.item(message.v);
          return;
        }
        case "event": {
          if (message.c === 1 && typeof message.n === "string") acknowledge(message.n);
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

    /** Acknowledges the coalesced event `name` at the next frame, with the others taken by then. */
    function acknowledge(name) {
      if (acks.size === 0) {
        nextFrame(() => {
          const n = [...acks];
          acks.clear();
          send({ t: "ack", k: token, n });
        });
      }
      acks.add(name);
    }

    /**
     * Resolves a call returning binary data with a typed array of its bytes:
     * those of the buffer the app shared, or fetched once from the URL `u`.
     */
    function binary(p, message, buffer) {
      const Type = TYPED_ARRAYS[message.b];
      const length = message.n;
      if (!Type || !Number.isInteger(length) || length < 0 || length % Type.BYTES_PER_ELEMENT !== 0) {
        p.reject(callError(p.method, `${p.method} failed`));
        return;
      }
      if (length === 0) {
        p.resolve(new Type(0));
      } else if (buffer instanceof ArrayBuffer && buffer.byteLength >= length) {
        p.resolve(new Type(buffer, 0, length / Type.BYTES_PER_ELEMENT));
      } else if (typeof message.u === "string") {
        fetch(message.u)
          .then((r) => {
            if (!r.ok) throw new Error(`HTTP ${r.status}`);
            return r.arrayBuffer();
          })
          .then(
            (bytes) => p.resolve(new Type(bytes, 0, length / Type.BYTES_PER_ELEMENT)),
            (err) => p.reject(callError(p.method, `${p.method}: its result could not be fetched: ${err.message}`)),
          );
      } else {
        p.reject(callError(p.method, `${p.method} failed`));
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
    return { runtime: Object.freeze({ call, on, stream }), receive, keyDown, dragOver, drop, token };
  }

  /** Whether a drag carries files from the system, rather than text or an element of the page. */
  function hasFiles(transfer) {
    return !!transfer && Array.from(transfer.types || []).includes("Files");
  }

  /** The typed arrays binary results may be, by name. */
  const TYPED_ARRAYS = Object.freeze({
    __proto__: null,
    Uint8Array,
    Int8Array,
    Int16Array,
    Uint16Array,
    Int32Array,
    Uint32Array,
    BigInt64Array,
    BigUint64Array,
    Float32Array,
    Float64Array,
  });

  /** The items a stream may send ahead of the page. */
  const STREAM_WINDOW = 16;

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

  /**
   * The message channel of the webview the page runs in, or null outside an
   * Oriel window: `post` sends a string to the app, `postFiles` a string
   * with File objects (null when the webview cannot), and `listen` hands
   * the app's messages to a function. docs/architecture.md says what a new
   * backend provides.
   */
  function findTransport(root) {
    // WebView2 (Windows): chrome.webview.
    const webview = root.chrome && root.chrome.webview;
    if (webview && typeof webview.postMessage === "function") {
      return {
        post: (message) => webview.postMessage(message),
        postFiles:
          typeof webview.postMessageWithAdditionalObjects === "function"
            ? (message, files) => webview.postMessageWithAdditionalObjects(message, files)
            : null, // an older runtime: drops reach the page only
        listen: (receive) => {
          webview.addEventListener("message", (e) => receive(e.data));
          // Binary results come in buffers the app shares, with their reply as additional data.
          webview.addEventListener("sharedbufferreceived", (e) => receive(e.additionalData, e.getBuffer()));
        },
      };
    }
    // WebKit (macOS, Linux): the script message handler "oriel" the backend
    // registers; it delivers messages by calling __orielReceive.
    const handler = root.webkit && root.webkit.messageHandlers && root.webkit.messageHandlers.oriel;
    if (handler && typeof handler.postMessage === "function") {
      return {
        post: (message) => handler.postMessage(message),
        // A drop's message goes alone: the backend has the paths of the drag.
        postFiles: (message) => handler.postMessage(message),
        listen: (receive) => Object.defineProperty(root, "__orielReceive", { value: receive }),
      };
    }
    return null;
  }

  /** Starts the runtime in a page: `window.oriel`, and the listeners it needs. */
  function start(root) {
    // Only the top document of an Oriel window talks to the app.
    const transport = findTransport(root);
    if (!transport || root.oriel || root.top !== root) return;
    const frame = typeof root.requestAnimationFrame === "function" ? (f) => root.requestAnimationFrame(f) : null;
    const { runtime, receive, keyDown, dragOver, drop } = createRuntime(transport.post, transport.postFiles, frame);
    transport.listen(receive);
    root.addEventListener("keydown", keyDown, true);
    root.addEventListener("dragover", dragOver, true);
    root.addEventListener("drop", drop, true);
    Object.defineProperty(root, "oriel", { value: runtime, enumerable: true });
  }

  if (typeof module === "object" && module.exports) {
    module.exports = { createRuntime, findTransport, start };
    return;
  }
  start(root);
})(globalThis);
