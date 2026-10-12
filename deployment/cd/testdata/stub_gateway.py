"""A small stand-in for the gateway, used only by test-scripts.sh to test smoke.sh.

It serves the routes the smoke checks call, accepts one synthetic account per role and records
each request line in the file named by STUB_LOG. STUB_BROKEN lists "METHOD /path" entries that
answer 500 and STUB_BAD_DISPLAY makes the public queue display return a wrong shape.
"""

import json
import os
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ACCOUNTS = {"receptionist-smoke": ("pw-r", "tok-r"), "doctor-smoke": ("pw-d", "tok-d")}
ROLE_OF = {"tok-r": "Receptionist", "tok-d": "Doctor"}
BROKEN = [entry for entry in os.environ.get("STUB_BROKEN", "").split(",") if entry]


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def reply(self, code, payload=None):
        data = b"" if payload is None else json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def role(self):
        header = self.headers.get("Authorization", "")
        return ROLE_OF.get(header.removeprefix("Bearer ").strip())

    def handle_request(self, method):
        path = self.path.split("?", 1)[0]
        with open(os.environ["STUB_LOG"], "a", encoding="utf-8") as log:
            log.write(f"{method} {path}\n")
        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length) if length else b""

        if f"{method} {path}" in BROKEN:
            return self.reply(500, {"message": "broken"})
        if method == "GET" and path == "/health":
            return self.reply(200, {"status": "Healthy"})
        if method == "POST" and path == "/api/auth/login":
            body = json.loads(raw or b"{}")
            account = ACCOUNTS.get(body.get("username"))
            if account and account[0] == body.get("password"):
                return self.reply(200, {"token": account[1], "user": {"role": "x"}})
            return self.reply(401, {"message": "Invalid username or password"})
        if method == "POST" and path == "/api/auth/logout":
            return self.reply(204) if self.role() else self.reply(401, {"message": "Unauthorized"})
        if method == "GET" and path == "/api/queue/display":
            if os.environ.get("STUB_BAD_DISPLAY"):
                return self.reply(200, {"currentRooms": "none"})
            return self.reply(200, {"currentRooms": [{"roomNumber": "1", "queueNumber": "A-001"}], "nextQueueNumbers": ["A-002"]})

        role = self.role()
        if role is None:
            return self.reply(401, {"message": "Unauthorized"})
        if method == "GET" and path == "/api/patients/search" and role == "Receptionist":
            return self.reply(200, [])
        if method == "GET" and path == "/api/queue/today" and role == "Receptionist":
            return self.reply(200, [])
        if method == "GET" and path == "/api/prescriptions/pending" and role == "Receptionist":
            return self.reply(200, [])
        if method == "GET" and path == "/api/notifications/today" and role == "Receptionist":
            return self.reply(200, [])
        if method == "GET" and path == "/api/templates" and role == "Doctor":
            return self.reply(200, [])
        return self.reply(403, {"message": "Forbidden"})

    def do_GET(self):
        self.handle_request("GET")

    def do_POST(self):
        self.handle_request("POST")


if __name__ == "__main__":
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    with open(sys.argv[1], "w", encoding="utf-8") as port_file:
        port_file.write(str(server.server_address[1]))
    server.serve_forever()
