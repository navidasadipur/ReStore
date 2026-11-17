export function getCookie(key: string) {
    var b = document.cookie.match("(^|;)\\s*" + key + "\\s*=\\s*([^;]+)");
    return b ? b.pop() : "";
}

// All API amounts are rials; the storefront displays tomans.
export const deliveryFeeInRials = 500_000;
export const freeDeliveryThresholdInRials = 10_000_000;

export function getDeliveryFee(subtotal: number) {
    return subtotal > freeDeliveryThresholdInRials ? 0 : deliveryFeeInRials;
}

export function currencyFormat(amount: number) {
    return new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 }).format(amount / 10) + ' تومان';
}
