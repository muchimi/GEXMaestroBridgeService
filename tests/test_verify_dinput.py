"""Regression checks for incomplete Windows GEX enumeration."""
import unittest

from verify_dinput import gex_inventory_failures, is_gex_product


class GexInventoryTests(unittest.TestCase):
    def test_twenty_created_but_ten_exposed_is_a_failure(self):
        expected = list(range(0x4701, 0x4715))
        failures = gex_inventory_failures(expected[:10], expected)
        self.assertEqual(len(failures), 1)
        self.assertIn('missing from DirectInput/joy.cpl', failures[0])
        self.assertIn('470B', failures[0])
        self.assertIn('4714', failures[0])

    def test_active_gaps_and_multiple_collections_are_valid(self):
        self.assertEqual(gex_inventory_failures([0x4702, 0x4704], [0x4702, 0x4702, 0x4704]), [])

    def test_complete_inventory_including_capacity_boundary_and_gamepads(self):
        products = list(range(0x4701, 0x4805))
        self.assertEqual(gex_inventory_failures(products, products), [])

    def test_order_must_be_observed(self):
        failures = gex_inventory_failures([0x4702, 0x4701], [0x4701, 0x4702])
        self.assertEqual(len(failures), 1)
        self.assertIn('Observed GEX DirectInput order', failures[0])

    def test_duplicate_directinput_products_are_rejected(self):
        failures = gex_inventory_failures([0x4701, 0x4701], [0x4701])
        self.assertEqual(len(failures), 1)
        self.assertIn('Duplicate GEX hardware PID', failures[0])

    def test_current_gex_families_only(self):
        for pid in (0x4701, 0x4800, 0x4801, 0x4804):
            self.assertTrue(is_gex_product(1, pid))
        for vid, pid in ((1, 0x4700), (1, 0x4805), (1, 0x4901), (0x1234, 0xBEAD)):
            self.assertFalse(is_gex_product(vid, pid))


if __name__ == '__main__':
    unittest.main()
