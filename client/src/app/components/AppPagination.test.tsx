import { render, screen } from '@testing-library/react';
import AppPagination from './AppPagination';

test('pagination follows server metadata when filters reset the page and displays an empty result correctly', () => {
    const { container, rerender } = render(<AppPagination metaData={{ currentPage: 3, pageSize: 6, totalCount: 14, totalPages: 3 }} onPageChange={jest.fn()} />);
    expect(screen.getByText('Displaying 13-14 of 14 items')).toBeInTheDocument();
    expect(container.querySelector('[aria-current="true"]')).toHaveTextContent('3');
    rerender(<AppPagination metaData={{ currentPage: 1, pageSize: 6, totalCount: 0, totalPages: 1 }} onPageChange={jest.fn()} />);
    expect(screen.getByText('Displaying 0-0 of 0 items')).toBeInTheDocument();
    expect(container.querySelector('[aria-current="true"]')).toHaveTextContent('1');
});
