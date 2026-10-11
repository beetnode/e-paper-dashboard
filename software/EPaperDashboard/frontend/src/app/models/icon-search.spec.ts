import { describe, expect, it } from 'vitest';
import { iconName, searchIcons } from './icon-search';

describe('iconName', () => {
  it('strips prefixes and style classes', () => {
    expect(iconName('fa-house')).toBe('house');
    expect(iconName('fa-solid fa-house')).toBe('house');
    expect(iconName('house')).toBe('house');
    expect(iconName(undefined)).toBe('');
  });
});

describe('searchIcons', () => {
  const names = ['house-chimney', 'house', 'temperature-half', 'thermometer', 'warehouse'];

  it('ranks names starting with the term first, then shorter names', () => {
    expect(searchIcons(names, 'house')).toEqual(['house', 'house-chimney', 'warehouse']);
  });

  it('requires every term and ignores an fa- prefix', () => {
    expect(searchIcons(names, 'fa-house chimney')).toEqual(['house-chimney']);
  });

  it('returns the first names up to the limit for an empty query', () => {
    expect(searchIcons(names, '', 2)).toEqual(['house-chimney', 'house']);
  });
});
