import { Alert, Box, Button, Paper, Step, StepLabel, Stepper, Typography } from "@mui/material";
import { LoadingButton } from "@mui/lab";
import { useEffect, useRef, useState } from "react";
import { FieldValues, FormProvider, useForm } from "react-hook-form";
import { yupResolver } from "@hookform/resolvers/yup";
import AddressForm from "./AddressForm";
import Review from "./Review";
import { validationSchema } from "./checkoutValidation";
import agent from "../../app/api/agent";
import { getFormErrors } from "../../app/api/contracts";
import { useAppSelector } from "../../app/store/configureStore";

const steps = ['Shipping address', 'Review your order'];

interface Props {
    redirectToPayment?: (url: string) => void;
}

export default function CheckoutPage({ redirectToPayment = url => window.location.assign(url) }: Props) {
    const [activeStep, setActiveStep] = useState(0);
    const [paymentMessage, setPaymentMessage] = useState('');
    const checkoutRequestId = useRef<string>();
    const orderId = useRef<number>();
    const submitting = useRef(false);
    const basket = useAppSelector(state => state.basket.basket);
    const methods = useForm({ mode: 'all', resolver: yupResolver(validationSchema) });
    const { reset, formState: { isValid, isSubmitting, isDirty }, setError } = methods;

    useEffect(() => {
        if (isDirty) return;
        let active = true;
        agent.Account.fetchAddress().then(address => {
            if (active && address) reset({ ...address, saveAddress: false });
        }).catch(() => { /* The buyer can enter the address manually. */ });
        return () => { active = false; };
    }, [isDirty, reset]);

    async function submit(data: FieldValues) {
        if (activeStep === 0) {
            setActiveStep(1);
            return;
        }
        if (submitting.current || (!orderId.current && !basket?.items.length)) return;
        submitting.current = true;
        setPaymentMessage('');
        const { saveAddress, fullName, address1, address2, city, state, zip, country } = data;
        try {
            checkoutRequestId.current ??= window.crypto.randomUUID();
            orderId.current ??= await agent.Orders.createPending({
                checkoutRequestId: checkoutRequestId.current,
                saveAddress: Boolean(saveAddress),
                shippingAddress: { fullName, address1, address2, city, state, zip, country }
            });
            const { paymentUrl } = await agent.Payments.startZarinpal(orderId.current);
            redirectToPayment(paymentUrl);
        } catch (error) {
            const failure = getFormErrors(error, ['fullName', 'address1', 'address2', 'city', 'state', 'zip', 'country']);
            setPaymentMessage(failure.message);
            Object.entries(failure.fieldErrors).forEach(([field, message]) => setError(field, { type: 'server', message }));
            if (Object.keys(failure.fieldErrors).length) setActiveStep(0);
        } finally {
            submitting.current = false;
        }
    }

    return (
        <FormProvider {...methods}>
            <Paper variant="outlined" sx={{ my: { xs: 3, md: 6 }, p: { xs: 2, md: 3 } }}>
                <Typography component="h1" variant="h4" align="center">Checkout</Typography>
                <Stepper activeStep={activeStep} sx={{ pt: 3, pb: 5 }}>
                    {steps.map(label => <Step key={label}><StepLabel>{label}</StepLabel></Step>)}
                </Stepper>
                <form onSubmit={methods.handleSubmit(submit)}>
                    {paymentMessage && <Alert severity="error" sx={{ mb: 2 }}>{paymentMessage}</Alert>}
                    {activeStep === 0 ? <AddressForm /> : <Review />}
                    <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1, mt: 3 }}>
                        {activeStep > 0 && !orderId.current && (
                            <Button disabled={isSubmitting} onClick={() => setActiveStep(0)}>Back</Button>
                        )}
                        <LoadingButton loading={isSubmitting} disabled={!isValid || (!orderId.current && !basket?.items.length)}
                            variant="contained" type="submit">
                            {activeStep === 0 ? 'Next' : 'Pay with ZarinPal'}
                        </LoadingButton>
                    </Box>
                </form>
            </Paper>
        </FormProvider>
    );
}
