const categories: Record<string, { label: string; image: string }> = {
  boards: { label: 'برد اسنوبرد', image: '/images/products/sb-react1.png' },
  boots: { label: 'بوت', image: '/images/products/boot-core1.png' },
  hats: { label: 'کلاه', image: '/images/products/hat-react1.png' },
  gloves: { label: 'دستکش', image: '/images/products/glove-code1.png' },
};

export function categoryDisplay(type: string) {
  const key = type.toLowerCase();
  return Object.prototype.hasOwnProperty.call(categories, key)
    ? categories[key]
    : { label: type, image: undefined };
}
