import { currencyFormat, getDeliveryFee } from './util';

test('shows API rials as tomans without losing fractional tomans', () => {
    expect(currencyFormat(1_000_000)).toBe('۱۰۰٬۰۰۰ تومان');
    expect(currencyFormat(19)).toBe('۱٫۹ تومان');
    expect(currencyFormat(0)).toBe('۰ تومان');
});

test('delivery becomes free only above one million tomans', () => {
    expect(getDeliveryFee(9_999_999)).toBe(500_000);
    expect(getDeliveryFee(10_000_000)).toBe(500_000);
    expect(getDeliveryFee(10_000_001)).toBe(0);
});
