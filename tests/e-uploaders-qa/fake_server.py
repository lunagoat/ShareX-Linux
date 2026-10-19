#!/usr/bin/env python3
import asyncio
import email
import http.server
import io
import json
import os
import re
import socket
import socketserver
import threading
import urllib.parse

class FakeHttpHandler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, format, *args):
        # Silence standard HTTP logs unless requested
        pass

    def do_GET(self):
        self.server.request_log.append({
            "method": "GET",
            "path": self.path,
            "headers": dict(self.headers)
        })
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path == "/health":
            self.send_response(200)
            self.send_header("Content-Type", "text/plain")
            self.end_headers()
            self.wfile.write(b"OK")
            return
        if parsed.path.startswith("/files/"):
            filename = parsed.path.split("/")[-1]
            data = self.server.stored_files.get(filename, b"Fake file content")
            self.send_response(200)
            self.send_header("Content-Type", "application/octet-stream")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)
            return

        self.send_response(200)
        self.send_header("Content-Type", "text/html")
        self.end_headers()
        self.wfile.write(b"<html><body>OK</body></html>")

    def do_POST(self):
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length) if length > 0 else b""
        self.server.request_log.append({
            "method": "POST",
            "path": self.path,
            "headers": dict(self.headers),
            "body": body
        })
        parsed = urllib.parse.urlparse(self.path)
        port = self.server.server_address[1]

        if parsed.path == "/upload/multipart":
            # Extract filename from Content-Disposition if multipart
            content_type = self.headers.get("Content-Type", "")
            filename = "uploaded_file.bin"
            match = re.search(rb'filename="([^"]+)"', body)
            if match:
                try:
                    filename = match.group(1).decode("utf-8")
                except UnicodeDecodeError:
                    filename = match.group(1).decode("latin-1")

            self.server.stored_files[filename] = body
            resp = {
                "status": "success",
                "files": [
                    {
                        "url": f"http://127.0.0.1:{port}/files/{filename}",
                        "name": filename
                    }
                ],
                "thumb": f"http://127.0.0.1:{port}/thumb/{filename}",
                "delete": f"http://127.0.0.1:{port}/delete/{filename}"
            }
            raw = json.dumps(resp).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)
            return

        if parsed.path == "/upload/json":
            # JSON upload
            resp = {
                "files": [
                    {
                        "url": f"http://127.0.0.1:{port}/files/stored-json"
                    }
                ],
                "thumb": f"http://127.0.0.1:{port}/thumb/stored-json",
                "delete": f"http://127.0.0.1:{port}/delete/stored-json"
            }
            raw = json.dumps(resp).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)
            return

        if parsed.path == "/upload/regex":
            # Plain text response for regex extraction
            text = f"status=success\nurl=http://127.0.0.1:{port}/files/stored-regex\n"
            raw = text.encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/plain")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)
            return

        if parsed.path == "/shorten":
            resp = {
                "short": f"http://127.0.0.1:{port}/s/short123",
                "url": f"http://127.0.0.1:{port}/s/short123"
            }
            raw = json.dumps(resp).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)
            return

        # Fallback POST
        resp = {"url": f"http://127.0.0.1:{port}/files/default"}
        raw = json.dumps(resp).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def do_PUT(self):
        # WebDAV PUT
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length) if length > 0 else b""
        self.server.request_log.append({
            "method": "PUT",
            "path": self.path,
            "headers": dict(self.headers),
            "body": body
        })
        filename = self.path.split("/")[-1]
        self.server.stored_files[filename] = body
        self.send_response(201)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_PROPFIND(self):
        # WebDAV PROPFIND
        self.server.request_log.append({
            "method": "PROPFIND",
            "path": self.path,
            "headers": dict(self.headers)
        })
        xml = b"""<?xml version="1.0" encoding="utf-8"?>
<d:multistatus xmlns:d="DAV:">
  <d:response>
    <d:href>/</d:href>
    <d:propstat>
      <d:prop>
        <d:resourcetype><d:collection/></d:resourcetype>
      </d:prop>
      <d:status>HTTP/1.1 200 OK</d:status>
    </d:propstat>
  </d:response>
</d:multistatus>"""
        self.send_response(207)
        self.send_header("Content-Type", "application/xml; charset=utf-8")
        self.send_header("Content-Length", str(len(xml)))
        self.end_headers()
        self.wfile.write(xml)


class FakeHttpServer:
    def __init__(self, port=0):
        self.server = socketserver.TCPServer(("127.0.0.1", port), FakeHttpHandler)
        self.server.request_log = []
        self.server.stored_files = {}
        self.port = self.server.server_address[1]
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)

    def start(self):
        self.thread.start()

    def stop(self):
        self.server.shutdown()
        self.server.server_close()


class FakeFtpProtocol(asyncio.Protocol):
    def __init__(self, stored_files):
        self.stored_files = stored_files
        self.transport = None
        self.pasv_server = None
        self.pasv_transport = None
        self.pasv_data = io.BytesIO()
        self.pasv_future = None
        self.user = None
        self.cwd = "/"

    def connection_made(self, transport):
        self.transport = transport
        self.transport.write(b"220 Fake FTP ready\r\n")

    def data_received(self, data):
        for line in data.decode("latin-1", errors="replace").splitlines():
            if not line:
                continue
            parts = line.strip().split(" ", 1)
            cmd = parts[0].upper()
            arg = parts[1] if len(parts) > 1 else ""
            self.handle_cmd(cmd, arg)

    def handle_cmd(self, cmd, arg):
        if cmd == "USER":
            self.user = arg
            self.transport.write(b"331 Password required\r\n")
        elif cmd == "PASS":
            self.transport.write(b"230 User logged in\r\n")
        elif cmd == "SYST":
            self.transport.write(b"215 UNIX Type: L8\r\n")
        elif cmd == "FEAT":
            self.transport.write(b"211-Features:\r\n UTF8\r\n211 End\r\n")
        elif cmd == "TYPE":
            self.transport.write(b"200 Type set\r\n")
        elif cmd == "PWD":
            self.transport.write(f'257 "{self.cwd}" is current directory\r\n'.encode("utf-8"))
        elif cmd == "CWD":
            self.cwd = arg if arg.startswith("/") else f"{self.cwd}/{arg}".replace("//", "/")
            self.transport.write(b"250 Directory changed\r\n")
        elif cmd == "MKD":
            self.transport.write(b"257 Directory created\r\n")
        elif cmd == "PASV":
            self.setup_pasv()
        elif cmd == "STOR":
            self.handle_stor(arg)
        elif cmd == "QUIT":
            self.transport.write(b"221 Goodbye\r\n")
            self.transport.close()
        elif cmd in ("NOOP", "OPTS"):
            self.transport.write(b"200 OK\r\n")
        else:
            self.transport.write(b"502 Command not implemented\r\n")

    def setup_pasv(self):
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.bind(("127.0.0.1", 0))
        sock.listen(1)
        pasv_port = sock.getsockname()[1]
        p1 = pasv_port // 256
        p2 = pasv_port % 256
        self.pasv_sock = sock
        self.transport.write(f"227 Entering Passive Mode (127,0,0,1,{p1},{p2})\r\n".encode("utf-8"))

    def handle_stor(self, filename):
        self.transport.write(b"150 Opening data connection\r\n")
        # Accept the data socket
        def accept_and_read():
            try:
                conn, _ = self.pasv_sock.accept()
                data = bytearray()
                while True:
                    chunk = conn.recv(65536)
                    if not chunk:
                        break
                    data.extend(chunk)
                conn.close()
                self.pasv_sock.close()
                self.stored_files[filename] = bytes(data)
                self.transport.write(b"226 Transfer complete\r\n")
            except Exception as e:
                self.transport.write(b"426 Connection closed; transfer aborted\r\n")

        threading.Thread(target=accept_and_read, daemon=True).start()


class FakeFtpServer:
    def __init__(self, port=0):
        self.port = port
        self.stored_files = {}
        self.server_thread = None
        self.loop = None
        self.actual_port = None
        self.ready_event = threading.Event()

    def run(self):
        self.loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.loop)

        async def main():
            server = await self.loop.create_server(
                lambda: FakeFtpProtocol(self.stored_files),
                "127.0.0.1",
                self.port
            )
            self.actual_port = server.sockets[0].getsockname()[1]
            self.ready_event.set()
            async with server:
                await server.serve_forever()

        try:
            self.loop.run_until_complete(main())
        except asyncio.CancelledError:
            pass

    def start(self):
        self.server_thread = threading.Thread(target=self.run, daemon=True)
        self.server_thread.start()
        self.ready_event.wait(timeout=5)

    def stop(self):
        if self.loop:
            self.loop.call_soon_threadsafe(self.loop.stop)


if __name__ == "__main__":
    http_srv = FakeHttpServer(0)
    http_srv.start()
    ftp_srv = FakeFtpServer(0)
    ftp_srv.start()
    print(f"HTTP Server running on http://127.0.0.1:{http_srv.port}")
    print(f"FTP Server running on ftp://127.0.0.1:{ftp_srv.actual_port}")
    try:
        import time
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        http_srv.stop()
        ftp_srv.stop()
