window.ollamaStream = {
    _currentController: null,
    _unloadBound: false,

    _bindUnload: function () {
        if (window.ollamaStream._unloadBound) {
            return;
        }

        window.addEventListener("beforeunload", function () {
            window.ollamaStream.cancelCurrent();
        });

        window.ollamaStream._unloadBound = true;
    },

    start: async function (url, payload, dotnetRef) {
        window.ollamaStream._bindUnload();

        if (window.ollamaStream._currentController) {
            window.ollamaStream._currentController.abort();
        }

        const controller = new AbortController();
        window.ollamaStream._currentController = controller;

        try {
            const response = await fetch(url, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload),
                signal: controller.signal
            });

            if (!response.ok) {
                const errorText = await response.text();
                const message = errorText && errorText.trim()
                    ? errorText.trim()
                    : "HTTP " + response.status;
                await dotnetRef.invokeMethodAsync("StreamError", message);
                return;
            }

            const reader = response.body.getReader();
            const decoder = new TextDecoder("utf-8");

            while (true) {
                const result = await reader.read();
                if (result.done) {
                    break;
                }
                const text = decoder.decode(result.value, { stream: true });
                if (text) {
                    await dotnetRef.invokeMethodAsync("AppendStream", text);
                }
            }

            await dotnetRef.invokeMethodAsync("CompleteStream");
        } catch (err) {
            if (err && err.name === "AbortError") {
                await dotnetRef.invokeMethodAsync("StreamCancelled");
                return;
            }

            await dotnetRef.invokeMethodAsync("StreamError", err && err.message ? err.message : "stream failed");
        } finally {
            if (window.ollamaStream._currentController === controller) {
                window.ollamaStream._currentController = null;
            }
        }
    },

    cancelCurrent: function () {
        if (window.ollamaStream._currentController) {
            window.ollamaStream._currentController.abort();
            window.ollamaStream._currentController = null;
        }
    }
};
