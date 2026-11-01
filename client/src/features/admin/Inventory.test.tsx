import { fireEvent, render, screen } from '@testing-library/react';
import Inventory from './Inventory';
import agent from '../../app/api/agent';

jest.mock('../../app/api/agent', () => ({ __esModule: true, default: { Catalog: { details: jest.fn() } } }));
jest.mock('../../app/store/configureStore', () => ({ useAppDispatch: () => jest.fn() }));
jest.mock('../../app/hooks/useProducts', () => () => ({
    products: [{ id: 7, name: 'Summary shoe', price: 300, brand: 'Brand', type: 'Shoes', quantityInStock: 10 }],
    metaData: null, error: null, retry: jest.fn()
}));
jest.mock('./ProductForm', () => (props: any) => <div>{props.product?.description}</div>);

test('editing a catalog summary first fetches the full product including its description', async () => {
    jest.mocked(agent.Catalog.details).mockResolvedValue({ id: 7, name: 'Summary shoe', price: 300,
        pictureUrl: '', brand: 'Brand', description: 'The full product description' });
    render(<Inventory />);
    fireEvent.click(screen.getByTestId('EditIcon').closest('button')!);
    expect(await screen.findByText('The full product description')).toBeInTheDocument();
    expect(agent.Catalog.details).toHaveBeenCalledWith(7);
});
