
function deleteCookie(cname) {
    setCookie(cname, 0, -1);
}

function setCookie(cname,cvalue,exdays) {
    const d = new Date();
    d.setTime(d.getTime() + (exdays*24*60*60*1000));
    const expires = "expires=" + d.toGMTString();
    document.cookie = cname + "=" + cvalue + ";" + expires + ";path=/; SameSite=Strict";
}

function getCookie(cname) {
    const name = cname + "=";
    const decodedCookie = decodeURIComponent(document.cookie);
    const ca = decodedCookie.split(';');
    for(const element of ca) {
        let c = element;
        while (c.charAt(0) === ' ') {
            c = c.substring(1);
        }
        if (c.indexOf(name) === 0) {
            return c.substring(name.length, c.length);
        }
    }
    return "";
}

function showReleaseNotes() {
    getReleaseNotes(function(text) {
        const c = getCookie("lastVersion");
        const firstTime = c ? new Date(c) : new Date(0);

        const json = JSON.parse(text);
        const itemsToShow = json.filter(function (item, index, array) {
            return new Date(item.published_at) > firstTime;
        });

        if (itemsToShow.length === 0)
            return;

        setCookie("lastVersion", itemsToShow[0].published_at, 1000);

        const latest = itemsToShow[0];
        const html = `New version <a  style='color:#8cbc13; text-decoration: underline' href='${latest.html_url}' target="releaseNotes">${latest.tag_name} ${latest.name}</a> has been released on ${new Date(latest.published_at).toLocaleString()}.<br /><br />${latest.body.replace(/\r\n/g, "<br/>")}`;

        SnackBar({
            message: html,
            dismissible: true,
            status: "Info",
            timeout: 120000,
            icon: "exclamation"
        });
    });
}

function getReleaseNotes(callback) {
    const xhttp = new XMLHttpRequest();
    xhttp.onreadystatechange = function() {
        if (this.readyState === 4 && this.status === 200) {
            callback( this.responseText);
        }
    };
    xhttp.open("GET", "https://api.github.com/repos/adrianoc/cecilifier/releases", true);
    xhttp.send();
}
  
function hideReleaseNotes() {
    document.getElementById("releaseNotesDiv").style.width = "0";
    document.getElementById("mainContent").style.visibility = "unset";
}