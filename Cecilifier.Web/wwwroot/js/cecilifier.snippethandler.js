function handleInitialSnippet(gist, errorAccessingGist, requestPath, removeStoredSnippet, defaultSnippet) {
    let snippet = gist;
    let autoCecilify = false;
    
    if (snippet !== null && snippet.length > 0) {
        if (errorAccessingGist.length !== 0)
            setError(errorAccessingGist);
        autoCecilify = true;
    }
    else {
        let snippetName = requestPath !== "/" ? requestPath.substring(1) : "default";
        if (removeStoredSnippet) {
            window.localStorage.removeItem(snippetName);
            SnackBar({
                message: `Snippet ${snippetName} deleted from local storage.`,
                dismissible: true,
                status: "Success",
                timeout: 10000,
                icon: "exclamation"
            });
            return;
        }
            
        snippet = window.localStorage.getItem(snippetName);
        
        if (snippet === null || snippet.length === 0) 
            snippet = defaultSnippet;

        onCecilifiySuccessCallbacks = onCecilifiySuccessCallbacks.concat(
            [
                {
                    state: snippetName,
                    callback: state => {
                        window.localStorage.setItem(state, csharpCode.getValue("\r\n"));
                    }
                }
            ]);
    }
    setValueFromSnippet(snippet, autoCecilify);
}

function setValueFromSnippet(snippet, autoCecilify) {
    if (snippet === null || snippet.length === 0)
        return;

    csharpCode.setValue(
        snippet
            .replace(/&quot;/g, '"')
            .replace(/&gt;/g, '>')
            .replace(/&lt;/g, '<')
            .replace(/&#x27;/g, "'")
            .replace(/&#x2B;/g, '+')
            .replace(/&#x38;/g, '&')
            .replace(/&amp;/g, '&'));

    if (autoCecilify)
        cecilifyFromSnippet(1);
}

function cecilifyFromSnippet(counter) {
    if (websocket.readyState !== WebSocket.OPEN) {
        if (counter < 4) {
            setTimeout(cecilifyFromSnippet, 500, counter + 1);
        }
        else {
        }
    }
    else {
        simulateClick("sendbutton");
    }
}