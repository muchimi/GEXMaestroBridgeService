import unittest
from osc_client import encode, decode


class CodecTests(unittest.TestCase):
    def test_golden_vector(self):
        golden = bytes.fromhex('2F7800002C69667300000000000000013F00000061000000')
        self.assertEqual(encode('/x', [1, 0.5, 'a']), golden)
        self.assertEqual(decode(golden), ('/x', [1, 0.5, 'a']))

    def test_bool_and_utf8_padding(self):
        args = [7, True, False, 'device', 'é', -1, 1.0]
        self.assertEqual(decode(encode('/maestro_test', args)), ('/maestro_test', args))

    def test_all_string_alignments(self):
        for n in range(20):
            packet = encode('/maestro_test', ['a' * n, 123])
            self.assertEqual(len(packet) % 4, 0)
            self.assertEqual(decode(packet)[1], ['a' * n, 123])


if __name__ == '__main__':
    unittest.main()
