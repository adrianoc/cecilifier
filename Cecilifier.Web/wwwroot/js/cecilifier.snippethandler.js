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

let savedSnippetSnack = null;
function showListOfLocallyStoredSnippets(editor) {
    let snippetNames = [];
    if (savedSnippetSnack !== null)
    {
        return;
    }

    for(let i = 0; i < window.localStorage.length; i++) {
        let key = window.localStorage.key(i);
        if (key.startsWith(saved_snippet_prefix)) {
            let snippetName= key.substring(saved_snippet_prefix.length); 
            snippetNames.push(
                `<div id="${snippetName}-to-remove" 
                        class="shortcut">
                        <a href="${snippetName}" title="Loads ${snippetName} snippet into editor"><i class="fa-solid fa-upload"></i></a> 
                        <a href="#" title="Removes ${snippetName} snippet." onclick="removeSnippet('${snippetName}');"><i class="fa-solid fa-trash-can"></i></a><a href="${snippetName}" title="Loads ${snippetName}snippet into editor"> ${snippetName}</a><br /></div>`);
        }
    }
    
    savedSnippetSnack = SnackBar({
        message: `<b>Saved snippets</b><br /><br/>${snippetNames.reduce( (acc, current) => `${acc}${current}`)}`,
        dismissible: true,
        status: "Info",
        timeout: false,
        icon: "!"
    });

    const closeSavedSnippetListAction = setupShortcutToCloseSaveSnippetList(editor);
    setupObserverToResetSavedSnippetListSnackUponClosing(closeSavedSnippetListAction);
}

function setupShortcutToCloseSaveSnippetList(editor) {

    const action = editor.addAction({
        id: "close-saved-snippet-list",

        label: "Close saved snippet list",

        keybindings: [monaco.KeyCode.KeyX],

        // A precondition for this action.
        precondition: null,

        // A rule to evaluate on top of the precondition in order to dispatch the keybindings.
        keybindingContext: null,

        contextMenuGroupId: "navigation",

        contextMenuOrder: 1.5,

        run: function (ed) {
            const temp = savedSnippetSnack;
            savedSnippetSnack = null;
            temp.Close();

        },
    });

    return action;
}

function setupObserverToResetSavedSnippetListSnackUponClosing(closeSavedSnippetListAction) {
    // To avoid showing multiple lists ideally we should be able to subscribe to a `closing` event on the snack with the list of saved snippets
    // and set `savedSnippetSnack` to null; unfortunately there's no such event whence we rely on implementation details of SnackBar and 
    // watch for changes in the style attribute of a element of the 'js-snackbar__wrapper' which SnackBar updates when the snack is closed.
    const snackMessage = Array.from(document.getElementsByClassName("js-snackbar__wrapper")).filter(element => element.innerHTML.indexOf("Saved snippets") !== -1).at(0);

    const config = { attributes: true, childList: false, characterData: false };

    const callback = mutations => {
        mutations.forEach(mutation => {
            if (mutation.attributeName === 'style') {
                savedSnippetSnack = null;
                closeSavedSnippetListAction.dispose();
            }
            if (mutation.attributeName === 'style')
                savedSnippetSnack = null;
        });
    };

    const observer = new MutationObserver(callback);
    observer.observe(snackMessage, config);
}

function removeSnippet(snippetName) {
    if (confirm(`Are you sure you want to remove the snippet '${snippetName}' ?`)) {
        window.localStorage.removeItem(saved_snippet_prefix + snippetName);
        let toBeRemoved = document.getElementById(`${snippetName}-to-remove`);
        toBeRemoved.parentNode.removeChild(toBeRemoved);
    }
}