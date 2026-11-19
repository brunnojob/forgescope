import unittest
from shop.orders import checkout

class OrderTest(unittest.TestCase):
    def test_total(self):
        self.assertEqual(checkout(10, 2), 20)
