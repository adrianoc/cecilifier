var websocket;
var sendButton;
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

    cecilifiedCode = CodeMirror.fromTextArea(
        document.getElementById("_cecilifiedcode"),
        {
            lineNumbers: true,
            matchBrackets: true,
            mode: "text/x-csharp",
            theme: "darcula"
        });


    initializeWebSocket();
}

function clearError() {
    setErrInternal(null);
}

function setError(str) {
    setErrInternal(str);
}

function setErrInternal(errorMsg) {
    var errorDiv = document.getElementById("cecilifier_error");

    if (errorMsg == null) {
        errorDiv.style.opacity = 0;
        errorDiv.style.position = "absolute";
        errorDiv.children[1].innerHTML = "";
    } else {
        errorDiv.style.opacity = 1;
        errorDiv.style.position = "relative";
        errorDiv.children[1].innerHTML = errorMsg;
        errorDiv.style.display = "block";
    }
}

function initializeWebSocket() {
    var scheme = document.location.protocol === "https:" ? "wss" : "ws";
    var port = document.location.port ? (":" + document.location.port) : "";
    var connectionURL = scheme + "://" + document.location.hostname + port + "/ws" ;

    websocket = new WebSocket(connectionURL);

    var sendButton = document.getElementById("sendbutton");
    sendButton.onclick = function() {
        send(websocket, 'C');
    };
    
    var downloadProjectButton = document.getElementById("downloadProject");
    downloadProjectButton.onclick = function() {
        send(websocket, 'Z');
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
        if (response.status == 0) {
            if (response.kind == 'Z') {
                setTimeout(function() {
                    var buttonId = create(base64ToArrayBuffer(response.cecilifiedCode), 'myfile.zip', 'application/zip');
                    simulateClick(buttonId);
                });
            }
            else {
                cecilifiedCode.setValue(response.cecilifiedCode);
            }
        } else if (response.status == 1) {
            setError(response.syntaxError.replace(/\n/g, "<br/>"));
        } else if (response.status == 2) {
            setError("Something went wrong. Please report the following error in the google group or in the git repository:\n" + response.error);
        }
    };
}

function send(websocket, format) {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) {
        alert("socket not connected");
        return;
    }
    clearError();

    websocket.send(format + csharpCode.getValue());
}
function create(text, name, type) {
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
