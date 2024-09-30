const saved_snippet_prefix = "saved-snippet-"; 
    
function handleInitialSnippet(gist, errorAccessingGist, requestPath, removeStoredSnippet, defaultSnippet) {
    let snippet = gist;
    let autoCecilify = false;

    if (errorAccessingGist.length !== 0) {
        const gistErrorToast = SnackBar({
            message: errorAccessingGist.replace(/(\r)?\n/g, "<br />"),
            dismissible: true,
            status: "Error",
            timeout: 60000
        });
        
        toastsToCleanUpBeforeCecilifying.push(gistErrorToast);
    }
    
    if (snippet !== null && snippet.length > 0) {
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

function showListOfLocallyStoredSnippets() {
    let snippetNames = [];
    for(let i = 0; i < window.localStorage.length; i++) {
        let key = window.localStorage.key(i);
        if (key.startsWith(saved_snippet_prefix)) {
            let snippetName= key.substring(saved_snippet_prefix.length); 
            snippetNames.push(`<div id="${snippetName}-to-remove" class="shortcut"><a href="#" title="Removes ${snippetName}." onclick="removeSnippet('${snippetName}');"><i class="fa-solid fa-trash-can"></i></a> <a href="${snippetName}" title="Loads ${snippetName}.">${snippetName}</a><br /></div>`);
        }
    }
    
    SnackBar({
        message: `<b>Saved snippets</b><br /><br/>${snippetNames.reduce( (acc, current) => `${acc}${current}`)}`,
        dismissible: true,
        status: "Info",
        timeout: false,
        icon: "!"
    });
}

function removeSnippet(snippetName) {
    if (confirm(`Are you sure you want to remove the snippet '${snippetName}' ?`)) {
        window.localStorage.removeItem(saved_snippet_prefix + snippetName);
        let toBeRemoved = document.getElementById(`${snippetName}-to-remove`);
        toBeRemoved.parentNode.removeChild(toBeRemoved);
    }
}