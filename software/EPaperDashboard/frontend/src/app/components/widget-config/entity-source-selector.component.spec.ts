import { groupByConnection } from './entity-source-selector.component';

describe('source grouping', () => {
  it('groups sources by connection in first-seen order', () => {
    const groups = groupByConnection([
      { id: 'a', connectionName: 'Home' },
      { id: 'b', connectionName: 'Office' },
      { id: 'c', connectionName: 'Home' },
      { id: 'd' },
    ]);
    expect(groups.map(g => g.name)).toEqual(['Home', 'Office', 'Other sources']);
    expect(groups[0].sources.map(s => s.id)).toEqual(['a', 'c']);
  });
});
