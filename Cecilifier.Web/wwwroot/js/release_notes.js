function setCookie(cname,cvalue,exdays) {
    var d = new Date();
    d.setTime(d.getTime() + (exdays*24*60*60*1000));
    var expires = "expires=" + d.toGMTString();
    document.cookie = cname + "=" + cvalue + ";" + expires + ";path=/";
}

function getCookie(cname) {
    var name = cname + "=";
    var decodedCookie = decodeURIComponent(document.cookie);
    var ca = decodedCookie.split(';');
    for(var i = 0; i < ca.length; i++) {
        var c = ca[i];
        while (c.charAt(0) == ' ') {
            c = c.substring(1);
        }
        if (c.indexOf(name) == 0) {
            return c.substring(name.length, c.length);
        }
    }
    return "";
}

function showReleaseNotes() {
    getReleaseNotes(function(text) {
        var c = getCookie("lastVersion");
        var firstTime = c ? new Date(c) : new Date(0);

        var json = JSON.parse(text);
        var itemsToShow = json.filter(function(item, index, array) {
            return new Date(item.published_at) > firstTime;
        });
        
        if (itemsToShow.length === 0)
            return;

        setCookie("lastVersion", itemsToShow[0].published_at, 1000);
        
        var latest = itemsToShow[0];
        var html = `New version <a href='${latest.html_url}' target="_blank">${latest.tag_name} ${latest.name}</a> has been released on ${new Date(latest.published_at).toLocaleString()}.`;
        setAlert("cecilifier_new_release", html);
    });
}

function getReleaseNotes(callback) {
    var xhttp = new XMLHttpRequest();
    xhttp.onreadystatechange = function() {
        if (this.readyState == 4 && this.status == 200) {
            callback( this.responseText);
        }
    };
    xhttp.open("GET", "https://api.github.com/repos/adrianoc/cecilifier/releases", true);
    xhttp.send();
}