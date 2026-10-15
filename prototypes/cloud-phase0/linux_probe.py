"""Real Unix socket/peer/permissions probe under WSL; no enforcement or daemon installation."""
import json
import os
import socket
import struct
import subprocess
import sys
import tempfile
import threading
import unittest

class UnixTransportProbe(unittest.TestCase):
    def test_private_socket_and_os_peer_identity(self):
        with tempfile.TemporaryDirectory(prefix='focusguard-cloud0-') as folder:
            os.chmod(folder, 0o700)
            path = os.path.join(folder, 'ipc.sock')
            with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as server:
                server.bind(path)
                os.chmod(path, 0o600)
                server.listen(1)
                server.settimeout(5)
                child_code = 'import socket,sys;s=socket.socket(socket.AF_UNIX);s.connect(sys.argv[1]);s.sendall(b"probe");assert s.recv(4)==b"ack!";s.close()'
                child = subprocess.Popen([sys.executable, '-c', child_code, path])
                try:
                    conn, _ = server.accept()
                    with conn:
                        conn.settimeout(5)
                        pid, uid, gid = struct.unpack('3i', conn.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, 12))
                        self.assertEqual(pid, child.pid)
                        self.assertEqual(uid, os.getuid())
                        self.assertEqual(gid, os.getgid())
                        self.assertEqual(conn.recv(5), b'probe')
                        conn.sendall(b'ack!')
                    self.assertEqual(child.wait(timeout=5), 0)
                    self.assertEqual(os.stat(folder).st_mode & 0o777, 0o700)
                    self.assertEqual(os.stat(path).st_mode & 0o777, 0o600)
                finally:
                    if child.poll() is None:
                        child.terminate()
                        child.wait(timeout=5)

if __name__ == '__main__':
    unittest.main()
