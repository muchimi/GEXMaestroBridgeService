#!/usr/bin/env python3
"""Dependency-free OSC client. Replies arrive on 8011, requests go to 8010."""
import argparse
import json
import secrets
import socket
import struct
import time
import uuid


def osc_string(value):
    data = value.encode('utf-8') + b'\0'
    return data + b'\0' * (-len(data) % 4)


def encode(address, args):
    tags, payload = ',', b''
    for arg in args:
        if isinstance(arg, bool):
            tags += 'T' if arg else 'F'
        elif isinstance(arg, int):
            tags += 'i'
            payload += struct.pack('>i', arg)
        elif isinstance(arg, float):
            tags += 'f'
            payload += struct.pack('>f', arg)
        elif isinstance(arg, str):
            tags += 's'
            payload += osc_string(arg)
        else:
            raise TypeError('Only int32, float32, strings and booleans are supported')
    return osc_string(address) + osc_string(tags) + payload


def decode(data):
    offset = 0

    def string():
        nonlocal offset
        end = data.index(0, offset)
        result = data[offset:end].decode('utf-8')
        offset = (end + 4) & ~3
        return result

    address, tags = string(), string()
    values = []
    for tag in tags[1:]:
        if tag == 's':
            values.append(string())
        elif tag in 'if':
            values.append(struct.unpack_from('>' + tag, data, offset)[0])
            offset += 4
        elif tag in 'TF':
            values.append(tag == 'T')
        else:
            raise ValueError('Unexpected reply type: ' + tag)
    return address, values


class Client:
    def __init__(self, host, receive_port, reply_bind, send_port):
        self.destination = (host, receive_port)
        self.tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.rx.bind((reply_bind, send_port))
        self.id = secrets.randbelow(1_000_000_000)

    def close(self):
        self.tx.close()
        self.rx.close()

    def request(self, address, *args):
        self.id += 1
        control = address.startswith(('/maestro_axis/', '/maestro_gamepad_axis/', '/maestro_button/', '/maestro_hat/'))
        packet = encode(address, args if control else [self.id, *args])
        rows, done = {}, None
        for _ in range(3):
            self.tx.sendto(packet, self.destination)
            deadline = time.monotonic() + 5
            while time.monotonic() < deadline:
                self.rx.settimeout(max(0.01, deadline - time.monotonic()))
                try:
                    data, source = self.rx.recvfrom(65535)
                except socket.timeout:
                    break
                if source[0] != socket.gethostbyname(self.destination[0]):
                    continue
                reply, values = decode(data)
                if reply == '/maestro_error' and len(values) >= 4 and ((control and values[1] == address) or (not control and values[0] == self.id)):
                    raise RuntimeError(f'{values[2]}: {values[3]}')
                if control:
                    if reply == address:
                        return [(reply, values)]
                    continue
                if not values or values[0] != self.id:
                    continue
                if reply == '/maestro_done':
                    done = values
                elif reply in ('/maestro_profile', '/maestro_device', '/maestro_system_device', '/maestro_definition'):
                    rows[values[1]] = (reply, values)
                else:
                    return [(reply, values)]
                if done is not None and len(rows) == done[4]:
                    return [rows[i] for i in sorted(rows)] + [('/maestro_done', done)]
        raise TimeoutError('No complete reply after 3 attempts; large/slow first-time device creation may need longer')


def smoke(client):
    """Requires --mock. Creates temporary named console and maximum-layout custom devices."""
    device = 'smoke-' + uuid.uuid4().hex[:12]
    client.request('/maestro_create', device, 'mock-gamepad', 'Smoke console')
    try:
        assert client.request(f'/maestro_button/{device}/A', -2.5)[0][1] == [1]
        assert client.request(f'/maestro_gamepad_axis/{device}/LX', 0.75)[0][1] == [0.75]
        assert client.request(f'/maestro_hat/{device}/1', 8)[0][1] == [8]
        assert client.request(f'/maestro_button/{device}/A')[0][1] == [1]
        client.request('/maestro_rename', device, 'Renamed console')
        assert client.request(f'/maestro_button/{device}/A')[0][1] == [0]
    finally:
        client.request('/maestro_delete', device)
    device += '-custom'
    client.request('/maestro_create_custom', device, 'Smoke HOTAS', 128, 8, 4)
    try:
        assert client.request(f'/maestro_button/{device}/128', 0.1)[0][1] == [1]
        assert client.request(f'/maestro_axis/{device}/Dial', 1.0)[0][1] == [1.0]
        assert client.request(f'/maestro_hat/{device}/4', 8)[0][1] == [8]
        assert client.request(f'/maestro_hat/{device}/4')[0][1] == [8]
        state = json.loads(client.request('/maestro_state', device)[0][1][2])
        assert state['pressedButtons'] == [128] and state['hats'] == [0, 0, 0, 8], state
        client.request('/maestro_remove', device)
        definitions = client.request('/maestro_definitions')
        assert any(r[0] == '/maestro_definition' and json.loads(r[1][2])['id'] == device for r in definitions)
        client.request('/maestro_activate', device)
        assert client.request(f'/maestro_button/{device}/128')[0][1] == [0]
    finally:
        client.request('/maestro_delete', device)
    print('PASS: mock UDP console + custom 128/8/4 + read/write + rename/delete integration')


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('address', nargs='?', default='/maestro_ping')
    p.add_argument('arguments', nargs='*', help='Each argument is JSON: integers, floats, true/false or quoted strings')
    p.add_argument('--host', default='127.0.0.1')
    p.add_argument('--receive-port', type=int, default=8010)
    p.add_argument('--send-port', type=int, default=8011)
    p.add_argument('--reply-bind', default='127.0.0.1')
    p.add_argument('--smoke', action='store_true')
    a = p.parse_args()
    client = Client(a.host, a.receive_port, a.reply_bind, a.send_port)
    try:
        if a.smoke:
            smoke(client)
        else:
            for address, args in client.request(a.address, *(json.loads(v) for v in a.arguments)):
                print(address, json.dumps(args))
    finally:
        client.close()


if __name__ == '__main__':
    main()
