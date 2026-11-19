from shop.pricing import total_v1

def legacy_checkout(price, quantity):
    return total_v1(price, quantity)
