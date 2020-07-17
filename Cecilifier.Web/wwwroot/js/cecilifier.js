var websocket;
var cecilifiedCode;
var csharpCode;

function initializeSite() {
    csharpCode = CodeMirror.fromTextArea(
        document.getElementById("_csharpcode"),
        {
            lineNumbers: true,
            matchBrackets: true,
            mode: "text/x-csharp",
            theme: "blackboard"
        });

    csharpCode.setOption("extraKeys", {
        "Ctrl-G": function(cm) {
            simulateClick("downloadProject");            
        },
        
        "Shift-Ctrl-C": function(cm) {
            simulateClick("sendbutton");
        }

    });
    
    cecilifiedCode = CodeMirror.fromTextArea(
        document.getElementById("_cecilifiedcode"),
        {
            lineNumbers: true,
            matchBrackets: true,
            mode: "text/x-csharp",
            theme: "darcula"
        });


    setSendToDiscordTooltip();
    
    initializeWebSocket();
}

function onSendToDiscordCheckBoxChanged()
{
    setSendToDiscordTooltip();
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

    let label = document.getElementById("postToDiscordLabel");
    label.setAttribute("data-tooltip", msg);
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

    websocket.send(format + (sendToDiscordOption ? 'A' : 'E') + csharpCode.getValue());
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
            .replace(/&#x38;/g, '&'));

    cecilifyFromGist(1);
}