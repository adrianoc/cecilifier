const saved_snippet_prefix = "saved-snippet-"; 
    
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
            window.localStorage.removeItem(saved_snippet_prefix + snippetName);
            SnackBar({
                message: `Snippet ${snippetName} deleted from local storage.`,
                dismissible: true,
                status: "Success",
                timeout: 10000,
                icon: "exclamation"
            });
            return;
        }
            
        snippet = window.localStorage.getItem(saved_snippet_prefix + snippetName);
        
        if (snippet === null || snippet.length === 0) 
            snippet = defaultSnippet;

        onCecilifiySuccessCallbacks = onCecilifiySuccessCallbacks.concat(
            [
                {
                    state: snippetName,
                    callback: state => {
                        window.localStorage.setItem(saved_snippet_prefix + state, csharpCode.getValue("\r\n"));
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

function showListOfLocalyStoredSnippets() {
    let snippetNames = [];
    for(let i = 0; i < window.localStorage.length; i++) {
        let key = window.localStorage.key(i);
        if (key.startsWith(saved_snippet_prefix)) {
            let snippetName= key.substring(saved_snippet_prefix.length); 
            snippetNames.push(`<a href="${snippetName}" class="shortcut">${snippetName}</a>`);
        }
    }
    
    SnackBar({
        message: `<b>List of saved snippets</b><br /><br/>${snippetNames.reduce( (acc, current) => `${acc}<br />${current}`)}`,
        dismissible: true,
        status: "Info",
        timeout: false,
        icon: "Info"
    });
}