// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/online").build();
connection.on("ReceiveMessage", function (message) {
    console.log("message", message);
});

connection.start().then(function () {
    console.log("connected");
    myFunction();

    setInterval(function () {
        myFunction()
    }, 10000)
}).catch(function (err) {
    return console.error(err.toString());
});

function myFunction() {
    let res = connection.invoke("SendMessage").catch(function (err) {
        return console.error(err.toString());
    });
}
