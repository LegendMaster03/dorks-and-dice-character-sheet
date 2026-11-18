"""Disposable localhost HTTP relay for S04 authorization-race characterization.

Only the Sheet-to-Site path is relayed. A successful real Site -> Rules Core
evaluation is held *after* Site has replied and *before* Sheet receives it,
allowing a deterministic role revocation in Site's actual SQLite database.

This is test-only. It is not a production authorization protocol or a remote
proxy. The caller must release the barrier even on test failure.
"""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from threading import Event, Thread
from urllib.request import Request, HTTPSHandler, ProxyHandler, build_opener
from urllib.error import HTTPError
from urllib.parse import urlsplit
import ssl


class S04SiteDelegationBarrier:
    ENDPOINT = "/api/mechanics/v1/origins/evaluate-approved-mixed"

    def __init__(self):
        self.reached = Event()
        self.release = Event()
        self.armed = False
        self.status = None
        owner = self

        class Relay(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def do_POST(self):
                self._relay()

            def do_GET(self):
                self._relay()

            def _relay(self):
                # Strictly local Site endpoint. Never forward arbitrary hosts,
                # client-supplied full URLs or record authorization headers.
                if (urlsplit(self.path).scheme or
                        not self.path.startswith("/") or
                        self.path.startswith("//")):
                    self.send_error(400)
                    return
                # ASP.NET HttpClient may stream JsonContent with chunked
                # transfer encoding. Decode it before relaying: stripping the
                # Transfer-Encoding header without reading chunks would drop
                # the actual mixed-Origin proposal and invalidate the test.
                transfer = self.headers.get("Transfer-Encoding", "").lower()
                if transfer and transfer != "chunked":
                    self.send_error(400)
                    return
                if transfer == "chunked":
                    chunks = []
                    size_so_far = 0
                    while True:
                        size_line = self.rfile.readline(100).split(b";", 1)[0]
                        try:
                            chunk_length = int(size_line.strip(), 16)
                        except ValueError:
                            self.send_error(400)
                            return
                        if chunk_length == 0:
                            while self.rfile.readline(8192) not in (b"\\r\\n", b"\\n", b""):
                                pass
                            break
                        size_so_far += chunk_length
                        if size_so_far > 2_000_000:
                            self.send_error(413)
                            return
                        chunks.append(self.rfile.read(chunk_length))
                        if self.rfile.read(2) != b"\\r\\n":
                            self.send_error(400)
                            return
                    data = b"".join(chunks)
                else:
                    length = int(self.headers.get("Content-Length", "0"))
                    if length < 0 or length > 2_000_000:
                        self.send_error(413)
                        return
                    data = self.rfile.read(length) if length else None
                headers = {
                    k: v for k, v in self.headers.items()
                    if k.lower() not in (
                        "host", "connection", "content-length",
                        "transfer-encoding", "accept-encoding")
                }
                req = Request("https://localhost:5157" + self.path,
                              data=data, headers=headers, method=self.command)
                opener = build_opener(
                    ProxyHandler({}),
                    HTTPSHandler(context=ssl._create_unverified_context()))
                try:
                    reply = opener.open(req, timeout=45)
                except HTTPError as error:
                    reply = error
                with reply:
                    status = reply.status
                    body = reply.read()
                    upstream_headers = list(reply.headers.items())

                if (owner.armed and self.command == "POST" and
                        owner.ENDPOINT in self.path and status == 200):
                    # Rules Core evaluation has finished and Site returned
                    # successfully. Sheet is still blocked awaiting this body.
                    owner.status = status
                    owner.reached.set()
                    if not owner.release.wait(timeout=30):
                        self.send_error(504, "S04 barrier was not released")
                        return

                self.send_response(status)
                for key, value in upstream_headers:
                    if key.lower() not in (
                            "connection", "transfer-encoding",
                            "content-length", "content-encoding",
                            "keep-alive"):
                        self.send_header(key, value)
                self.send_header("Content-Length", str(len(body)))
                self.send_header("Connection", "close")
                self.end_headers()
                try:
                    self.wfile.write(body)
                except BrokenPipeError:
                    pass
                self.close_connection = True

            def log_message(self, format, *args):
                # Suppress paths, bearer credentials, ticket headers and
                # connection metadata in the CI evidence artifact.
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 5160), Relay)
        self.thread = Thread(target=self.server.serve_forever, daemon=True)

    def start(self):
        self.thread.start()

    def arm(self):
        self.reached.clear()
        self.release.clear()
        self.status = None
        self.armed = True

    def close(self):
        self.release.set()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)
