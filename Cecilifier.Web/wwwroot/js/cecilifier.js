let websocket;
var cecilifiedCode;
var csharpCode;

class CecilifierRequest
{
    constructor(code, options) {
        this.code = code;
        this.options = options;
    }
}

class WebOptions
{
    constructor(deployKind, publishSourcePolicy) {
        this.deployKind = deployKind;
        this.publishSourcePolicy = publishSourcePolicy;
    }
}

function initializeSite(errorAccessingGist, gist, version) {
    require.config({ paths: { vs: 'lib/node_modules/monaco-editor/min/vs' } });

    require(['vs/editor/editor.main'], function () {
        csharpCode = monaco.editor.create(document.getElementById('csharpcode'), {
            theme: "vs-dark",
            value: ['using System;', 'class Foo', '{', '\tvoid Bar() => Console.WriteLine("Hello World!");', '}'].join('\n'),
            language: 'csharp',
            minimap: { enabled: false },
            fontSize: 16,
            glyphMargin: true,
        });
        
        cecilifiedCode = monaco.editor.create(document.getElementById('cecilifiedcode'), {
            theme: "vs-dark",
            value: "",
            language: 'csharp',
            readOnly: true,
            minimap: { enabled: false },
            fontSize: 16,
            glyphMargin: true,
         });
         
        // Configure keyboard shortcuts
        csharpCode.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KEY_D, function() {
            simulateClick("downloadProject");
        });

        csharpCode.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KEY_C, function() {
            simulateClick("sendbutton");
        });

        csharpCode.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.US_OPEN_SQUARE_BRACKET , function() {
            var options = csharpCode.getRawOptions();
            var newFontSize = Math.ceil(options.fontSize - options.fontSize * 0.05);
            
            csharpCode.updateOptions({ fontSize: newFontSize });
        });

        csharpCode.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.US_CLOSE_SQUARE_BRACKET , function() {
            var options = csharpCode.getRawOptions();

            var newFontSize = Math.ceil(options.fontSize * 1.05);
            csharpCode.updateOptions({ fontSize: newFontSize });
        });

        // Handle gist
        if (errorAccessingGist.length === 0) {            
            setValueFromGist(gist);
        } else {
            setError(errorAccessingGist);
        }

        window.onresize = function(ev) {
            updateEditorsSize();
        }

        updateEditorsSize();        
    });
    
    setTooltips(version);

    setSendToDiscordTooltip();   
    
    initializeWebSocket();
    disableScroll();
}

function disableScroll() {
    // Get the current page scroll position
    scrollTop = window.pageYOffset || document.documentElement.scrollTop;
    scrollLeft = window.pageXOffset || document.documentElement.scrollLeft,
  
        // if any scroll is attempted, set this to the previous value
        window.onscroll = function() {
            window.scrollTo(scrollLeft, scrollTop);
        };
}
  
function enableScroll() {
    window.onscroll = function() {};
}

function updateEditorsSize() {
    var csharpCodeDiv = document.getElementById("csharpcode");
    let h = window.innerHeight * 0.35;
    csharpCodeDiv.style.height = `${h}px`;

    var cecilifiedCodeDiv = document.getElementById("cecilifiedcode");
    cecilifiedCodeDiv.style.height = `${window.innerHeight - h - 80}px`;

    csharpCode.layout();
    cecilifiedCode.layout();
}

function setTooltips(version) {
    let msg = `Cecilifier version ${version}<br/><br/>Cecilifier is meant to make it easier to learn how to use <a href="https://github.com/jbevain/cecil" target="_blank">Mono.Cecil</a>. You can read more details about it in its <a href="https://programing-fun.blogspot.com/2019/02/making-it-easier-to-getting-started.html" target="_blank">blog announcement</a>.`;
    
    let defaultDelay =  [500, null];
    tippy('#aboutSpan2', {
        content: msg,
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });
    
    tippy('#csharpcode-container', {
        content: "Type any valid C# code to generate the equivalent Cecil api calls.",
        placement: 'bottom',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#cecilifiedcode-container', {
        content: "After pressing <i>Cecilify your code!</i> button, the generate code will be show here.",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#sendbutton', {
        content: "After entering the code you want to process, press this button. (Ctrl-Shift-C)",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#downloadProject', {
        content: "Use this option if you want to download a .Net Core 3.0 project, ready for you to play with! (Ctrl-Shift-D)",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#copyToClipboard', {
        content: "Copy cecilified code to clipboard.",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });    
}

function setSendToDiscordTooltip()
{
    let msgBody = "the source from the top textbox will be sent to an internal discord channel (accessible only to Cecilifier's author)";
    let msg = null;
    if (getSendToDiscordValue())
    {
        msg = "Publish On: " + msgBody +  ". Preferable if the contents of the code is not sensitive. This helps Cecilifier developer to better understand usage pattern.";
    }
    else
    {
        msg = "Publish Off: " + msgBody +  " only in case of errors.";
    }    
    
    msg = msg + "\r\nClick on the link at the bottom of this page to join the general discussion discord channel.";

    tippy('#postToDiscordLabel', {
        content: msg,
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip'
    });    
}

function onSendToDiscordCheckBoxChanged()
{
    let instance = document.querySelector('#postToDiscordLabel')._tippy;
    instance.destroy();
    setSendToDiscordTooltip();
    
    instance = document.querySelector('#postToDiscordLabel')._tippy;
    instance.show();
}

function getSendToDiscordValue()
{
    let checkbox = document.getElementById("postToDiscord");
    return checkbox.checked;
}

function setAlert(div_id, msg) {
    var div = document.getElementById(div_id);

    if (msg == null) {
        div.style.opacity = "0";
        div.style.position = "absolute";
        div.children[1].innerHTML = "";
    } else {
        div.style.opacity = "1";
        div.style.position = "relative";
        div.children[1].innerHTML = msg;
        div.style.display = "block";
    }
}

function clearError() {
    setAlert("cecilifier_error", null);
}

function setError(str) {
    setAlert("cecilifier_error", str);
}

function initializeWebSocket() {
    var scheme = document.location.protocol === "https:" ? "wss" : "ws";
    var port = document.location.port ? (":" + document.location.port) : "";
    var connectionURL = scheme + "://" + document.location.hostname + port + "/ws" ;

    websocket = new WebSocket(connectionURL);

    var sendButton = document.getElementById("sendbutton");
    sendButton.onclick = function() {
        send(websocket, 'C', getSendToDiscordValue());
    };
    
    var downloadProjectButton = document.getElementById("downloadProject");
    downloadProjectButton.onclick = function() {
        send(websocket, 'Z', getSendToDiscordValue());
    };

    websocket.onopen = function (event) {
    };

    websocket.onclose = function (event) {
    };

    websocket.onerror = function(event) {
        console.error("WebSocket error observed:", event);
    };
    
    websocket.onmessage = function (event) {
        // this is where we get the cecilified code back...
        var response = JSON.parse(event.data);
        if (response.status === 0) {
            var cecilifiedCounter = document.getElementById('cecilified_counter');
            cecilifiedCounter.innerText = response.counter;

            if (response.kind === 'Z') {
                setTimeout(function() {
                    var buttonId = createProjectZip(base64ToArrayBuffer(response.cecilifiedCode), response.mainTypeName + ".zip", 'application/zip');
                    simulateClick(buttonId);
                });
            }
            else {
                cecilifiedCode.setValue(response.cecilifiedCode);
            }
        } else if (response.status === 1) {
            setError(response.syntaxError.replace(/\n/g, "<br/>"));
        } else if (response.status === 2) {
            setError("Something went wrong. Please report the following error in the google group or in the git repository:\n" + response.error);
        }
    };
}

function send(websocket, format, sendToDiscordOption) {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) {
        alert("socket not connected");
        return;
    }
    clearError();

    var request = new CecilifierRequest(csharpCode.getValue(), new WebOptions(format, sendToDiscordOption ? 'A' : 'E'));
    websocket.send(JSON.stringify(request));
}

function createProjectZip(text, name, type) {
    var buttonId = "dlbtn";
    var dlbtn = document.getElementById(buttonId);
    var file = new Blob([text], {type: type});
    dlbtn.href = URL.createObjectURL(file);
    dlbtn.download = name;
    
    return buttonId;
}

function base64ToArrayBuffer(base64) {
    var binary_string =  window.atob(base64);
    var len = binary_string.length;
    var bytes = new Uint8Array( len );
    for (var i = 0; i < len; i++)        {
        bytes[i] = binary_string.charCodeAt(i);
    }
    return bytes.buffer;
}

function simulateClick(elementId) {
    var event = new MouseEvent('click', {
        view: window,
        bubbles: true,
        cancelable: true
    });
    var cb = document.getElementById(elementId);
    var cancelled = !cb.dispatchEvent(event);
}

function copyToClipboard(elementId) {
    navigator.clipboard.writeText(cecilifiedCode.getValue("\r\n"));
}

function cecilifyFromGist(counter) {
    if (websocket.readyState !== WebSocket.OPEN) {
        if (counter < 4) {
            setTimeout(cecilifyFromGist, 500, counter + 1);
        }
    }
    else {
        simulateClick("sendbutton");
    }
}

function setValueFromGist(snipet) {
    if (snipet === null || snipet.length === 0)
        return;

    csharpCode.setValue(
            snipet
            .replace(/&quot;/g, '"')
            .replace(/&gt;/g, '>')
            .replace(/&lt;/g, '<')
            .replace(/&#x27;/g, "'")
            .replace(/&#x2B;/g, '+')
            .replace(/&#x38;/g, '&')
            .replace(/&amp;/g, '&'));

    cecilifyFromGist(1);
}